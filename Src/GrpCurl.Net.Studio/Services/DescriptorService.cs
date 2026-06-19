using Google.Protobuf.Reflection;
using Grpc.Core;
using GrpCurl.Net.DescriptorSources;
using GrpCurl.Net.Invocation;
using GrpCurl.Net.Output;
using GrpCurl.Net.Studio.ViewModels.Models.Connections;
using GrpCurl.Net.Studio.ViewModels.Models.Descriptors;
using GrpCurl.Net.Studio.ViewModels.Services;
using GrpCurl.Net.Utilities;
using System.Diagnostics;

namespace GrpCurl.Net.Studio.Services;

/// <summary>
///     Reflection-backed <see cref="IDescriptorService" />. Builds the catalog and per-symbol
///     descriptions through Core's <see cref="DescriptorSourceFactory" /> using the connection's full
///     channel options, so Studio sees exactly what the CLI <c>list</c>/<c>describe</c> would. The
///     session (and its channel) is disposed once read; the long-lived business channel for
///     invocation arrives with E1.4.
/// </summary>
internal sealed class DescriptorService(ITlsProfileResolver? tlsResolver = null, ISettingsStore? settings = null) : IDescriptorService
{
    public async Task<DescriptorLoadResult> LoadAsync(SavedConnection connection, CancellationToken cancellationToken = default)
    {
        var (profile, password) = await ResolveTlsAsync(connection, cancellationToken).ConfigureAwait(false);
        var options = ConnectionChannelMapper.ToChannelOptions(connection, maxMessageSize: null, profile, password);
        var metadata = ConnectionChannelMapper.BuildReflectionMetadata(connection);
        var (protosets, protos, imports) = ConnectionChannelMapper.DescriptorPaths(connection);
        var warnings = new CollectingWarningSink();
        var stopwatch = Stopwatch.StartNew();

        try
        {
            await using var session = await DescriptorSourceFactory.CreateAsync(
                connection.Address,
                protosetPaths: protosets,
                protoFiles: protos,
                importPaths: imports,
                channelOptions: options,
                reflectionMetadata: metadata,
                cancellationToken: cancellationToken,
                warningSink: warnings,
                descriptorOptions: BuildDescriptorOptions(connection)).ConfigureAwait(false);

            var serviceNames = await session.Source.ListServicesAsync(cancellationToken).ConfigureAwait(false);
            var services = new List<ServiceEntry>(serviceNames.Count);

            foreach (var name in serviceNames)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (await session.Source.FindSymbolAsync(name, cancellationToken).ConfigureAwait(false) is ServiceDescriptor descriptor)
                {
                    services.Add(MapService(descriptor));
                }
            }

            var types = CollectTypes(session.Source.FileDescriptorSet);
            stopwatch.Stop();

            var catalog = new ServiceCatalog(services, warnings.Messages)
            {
                Types = types,
                FileCount = session.Source.FileDescriptorSet?.File.Count ?? 0,
                SymbolCount = services.Sum(s => 1 + s.Methods.Count) + types.Count,
                LoadDuration = stopwatch.Elapsed
            };

            return DescriptorLoadResult.Success(catalog);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw; // user cancellation — the VM treats this as "no longer loading", not an error
        }
        catch (RpcException ex)
        {
            return DescriptorLoadResult.Failure(MapRpcError(ex));
        }
        catch (Exception ex) when (IsDescriptorSourceError(ex))
        {
            return DescriptorLoadResult.Failure(MapDescriptorSourceError(ex));
        }
    }

    public async Task<DescribeResult> DescribeAsync(SavedConnection connection, string symbol, CancellationToken cancellationToken = default)
    {
        var (profile, password) = await ResolveTlsAsync(connection, cancellationToken).ConfigureAwait(false);
        var options = ConnectionChannelMapper.ToChannelOptions(connection, maxMessageSize: null, profile, password);
        var metadata = ConnectionChannelMapper.BuildReflectionMetadata(connection);
        var (protosets, protos, imports) = ConnectionChannelMapper.DescriptorPaths(connection);
        var warnings = new CollectingWarningSink();

        try
        {
            await using var session = await DescriptorSourceFactory.CreateAsync(
                connection.Address,
                protosetPaths: protosets,
                protoFiles: protos,
                importPaths: imports,
                channelOptions: options,
                reflectionMetadata: metadata,
                cancellationToken: cancellationToken,
                warningSink: warnings,
                descriptorOptions: BuildDescriptorOptions(connection)).ConfigureAwait(false);

            // Accept both the dotted FQN and the invocation grammar (pkg.Service/Method) for methods.
            var normalized = symbol.Replace('/', '.');
            var descriptor = await session.Source.FindSymbolAsync(normalized, cancellationToken).ConfigureAwait(false);

            return descriptor switch
            {
                ServiceDescriptor s => DescribeResult.Success(MapServiceDescription(s)),
                MethodDescriptor m  => DescribeResult.Success(MapMethodDescription(m)),
                MessageDescriptor g => DescribeResult.Success(MapMessageDescription(g)),
                EnumDescriptor e    => DescribeResult.Success(MapEnumDescription(e)),
                null                => DescribeResult.Failure(new DescriptorLoadError(
                    $"Symbol '{symbol}' was not found in the active descriptor set.", Hint: null, ReflectionUnavailable: false)),
                _                   => DescribeResult.Failure(new DescriptorLoadError(
                    $"'{symbol}' is a {descriptor.GetType().Name}, which cannot be described.", Hint: null, ReflectionUnavailable: false))
            };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (RpcException ex)
        {
            return DescribeResult.Failure(MapRpcError(ex));
        }
        catch (Exception ex) when (IsDescriptorSourceError(ex))
        {
            return DescribeResult.Failure(MapDescriptorSourceError(ex));
        }
    }

    public async Task<SchemaExportResult> ExportProtosetAsync(SavedConnection connection, string path, bool overwrite, CancellationToken cancellationToken = default)
    {
        try
        {
            // Refuse-by-default: gate the overwrite at the app layer so we can report size/mtime (FR-101).
            if (!overwrite && File.Exists(path))
            {
                return SchemaExportResult.Conflict([ToConflict(path)]);
            }

            var stopwatch = Stopwatch.StartNew();
            await using var session = await OpenSessionAsync(connection, cancellationToken).ConfigureAwait(false);

            // Empty symbols => the whole active set, byte-parity with the CLI's --protoset-out.
            await ProtosetExporter.WriteProtosetAsync(session.Source, path, force: true, symbols: [], cancellationToken)
                .ConfigureAwait(false);

            stopwatch.Stop();
            return SchemaExportResult.Success([ToExported(path)], stopwatch.Elapsed);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (IsExportError(ex))
        {
            return SchemaExportResult.Failure(ex.Message);
        }
    }

    public async Task<SchemaExportResult> ExportProtosAsync(SavedConnection connection, string directory, bool overwrite, CancellationToken cancellationToken = default)
    {
        try
        {
            var stopwatch = Stopwatch.StartNew();
            await using var session = await OpenSessionAsync(connection, cancellationToken).ConfigureAwait(false);

            // Warm the source so a reflection set populates its FileDescriptorSet before we read it.
            _ = await session.Source.ListServicesAsync(cancellationToken).ConfigureAwait(false);

            if (!overwrite)
            {
                var conflicts = WouldBeProtoPaths(session.Source, directory)
                    .Where(File.Exists)
                    .Select(ToConflict)
                    .ToList();

                if (conflicts.Count > 0)
                {
                    return SchemaExportResult.Conflict(conflicts);
                }
            }

            await ProtoFileEmitter.WriteAsync(session.Source, directory, force: true, cancellationToken).ConfigureAwait(false);

            stopwatch.Stop();

            var written = Directory.Exists(directory)
                ? Directory.GetFiles(directory, "*.proto", SearchOption.AllDirectories).Select(ToExported).ToList()
                : [];

            return SchemaExportResult.Success(written, stopwatch.Elapsed);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (IsExportError(ex))
        {
            return SchemaExportResult.Failure(ex.Message);
        }
    }

    public async Task<string?> GetProtoSnippetAsync(SavedConnection connection, string symbol, CancellationToken cancellationToken = default)
    {
        try
        {
            await using var session = await OpenSessionAsync(connection, cancellationToken).ConfigureAwait(false);

            var normalized = symbol.Replace('/', '.');
            var descriptor = await session.Source.FindSymbolAsync(normalized, cancellationToken).ConfigureAwait(false);

            // Emit the whole defining file — the same reconstruction the export uses, byte-for-byte.
            var file = descriptor switch
            {
                ServiceDescriptor s => s.File,
                MethodDescriptor m => m.File,
                MessageDescriptor g => g.File,
                EnumDescriptor e => e.File,
                _ => null
            };

            return file is null ? null : ProtoFileEmitter.EmitFile(file);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (IsDescriptorSourceError(ex) || ex is RpcException)
        {
            return null;
        }
    }

    /// <summary>Opens a descriptor session with the connection's full channel + descriptor-source config.</summary>
    private async Task<DescriptorSourceFactory> OpenSessionAsync(SavedConnection connection, CancellationToken cancellationToken)
    {
        var (profile, password) = await ResolveTlsAsync(connection, cancellationToken).ConfigureAwait(false);
        var options = ConnectionChannelMapper.ToChannelOptions(connection, maxMessageSize: null, profile, password);
        var metadata = ConnectionChannelMapper.BuildReflectionMetadata(connection);
        var (protosets, protos, imports) = ConnectionChannelMapper.DescriptorPaths(connection);

        return await DescriptorSourceFactory.CreateAsync(
            connection.Address, protosets, protos, imports, options, metadata, cancellationToken,
            descriptorOptions: BuildDescriptorOptions(connection)).ConfigureAwait(false);
    }

    /// <summary>
    ///     Resolves the effective descriptor caps: a per-connection override (FR-049) wins; otherwise the
    ///     app-wide default (FR-157, from settings); otherwise Core's default. Returns null only when no
    ///     app-wide setting is wired and the connection sets no override (so Core uses its own defaults).
    /// </summary>
    internal DescriptorSourceOptions? BuildDescriptorOptions(SavedConnection connection)
    {
        var c = connection.DescriptorSource;
        var hasOverride = c.MaxProtosetFileBytes is not null || c.MaxReflectionDescriptorBytes is not null
            || c.MaxFileDescriptors is not null || c.MaxDependencyDepth is not null || c.MaxSymbols is not null;

        if (!hasOverride && settings is null)
        {
            return null; // no overrides and no app-wide settings — Core uses its defaults
        }

        // The app-wide default layer (FR-157); falls back to Core's defaults when settings aren't wired.
        var app = settings?.Current.DescriptorLimits;
        var d = DescriptorSourceOptions.Default;

        return new DescriptorSourceOptions
        {
            MaxProtosetFileBytes = c.MaxProtosetFileBytes ?? app?.MaxProtosetFileBytes ?? d.MaxProtosetFileBytes,
            MaxReflectionDescriptorBytes = c.MaxReflectionDescriptorBytes ?? app?.MaxReflectionDescriptorBytes ?? d.MaxReflectionDescriptorBytes,
            MaxFileDescriptors = c.MaxFileDescriptors ?? app?.MaxFileDescriptors ?? d.MaxFileDescriptors,
            MaxDependencyDepth = c.MaxDependencyDepth ?? app?.MaxDependencyDepth ?? d.MaxDependencyDepth,
            MaxSymbols = c.MaxSymbols ?? app?.MaxSymbols ?? d.MaxSymbols
        };
    }

    // ProtoFileEmitter writes one .proto per descriptor file at dir/<file.Name>; mirror that to pre-flight
    // the conflict list (a superset of the service-reachable files is fine — it only over-warns).
    private static IEnumerable<string> WouldBeProtoPaths(IDescriptorSource source, string directory)
        => source.FileDescriptorSet is { } set
            ? set.File.Select(f => Path.GetFullPath(Path.Combine(directory, f.Name)))
            : [];

    private static bool IsExportError(Exception ex)
        => ex is IOException or UnauthorizedAccessException or InvalidOperationException
            or RpcException or ProtocNotFoundException;

    private static ExportedFile ToExported(string path)
    {
        var info = new FileInfo(path);
        return new ExportedFile(path, info.Exists ? info.Length : 0);
    }

    private static FileConflict ToConflict(string path)
    {
        var info = new FileInfo(path);
        return new FileConflict(path, info.Exists ? info.Length : 0, info.Exists ? info.LastWriteTimeUtc : DateTime.MinValue);
    }

    private async Task<(TlsProfile? Profile, string? Password)> ResolveTlsAsync(SavedConnection connection, CancellationToken cancellationToken)
        => tlsResolver is null ? default : await tlsResolver.ResolveAsync(connection, cancellationToken).ConfigureAwait(false);

    // Local descriptor-source failures (FR-042/FR-044): protoc missing, proto compile error, or an
    // unreadable/invalid protoset file — all schema-category, surfaced with the verbatim detail.
    private static bool IsDescriptorSourceError(Exception ex)
        => ex is ProtocNotFoundException or InvalidOperationException or IOException;

    private static DescriptorLoadError MapDescriptorSourceError(Exception ex)
        => new(
            ex.Message,
            ex is ProtocNotFoundException
                ? "Set a protoc path in Settings → protoc, or configure a protoset instead."
                : null,
            ReflectionUnavailable: false);

    private static ServiceEntry MapService(ServiceDescriptor descriptor)
    {
        var methods = descriptor.Methods
            .Select(m => new ServiceMethod(
                m.Name,
                $"{descriptor.FullName}/{m.Name}",
                StreamingShapeExtensions.FromFlags(m.IsClientStreaming, m.IsServerStreaming),
                m.InputType.FullName,
                m.OutputType.FullName,
                m.GetOptions()?.Deprecated == true))
            .ToList();

        return new ServiceEntry(descriptor.FullName, methods, descriptor.GetOptions()?.Deprecated == true);
    }

    // --- describe mapping (FR-050/052) ---

    private static ServiceDescription MapServiceDescription(ServiceDescriptor s)
    {
        var methods = s.Methods
            .Select(m => new MethodSummary(
                m.Name,
                $"{s.FullName}/{m.Name}",
                StreamingShapeExtensions.FromFlags(m.IsClientStreaming, m.IsServerStreaming),
                new TypeRef(m.InputType.FullName, Resolvable: true),
                new TypeRef(m.OutputType.FullName, Resolvable: true),
                m.GetOptions()?.Deprecated == true))
            .ToList();

        return new ServiceDescription(s.FullName, s.Name, s.File.Name, methods, s.GetOptions()?.Deprecated == true);
    }

    private static MethodDescription MapMethodDescription(MethodDescriptor m)
        => new(
            m.FullName,
            m.Name,
            m.File.Name,
            StreamingShapeExtensions.FromFlags(m.IsClientStreaming, m.IsServerStreaming),
            new TypeRef(m.InputType.FullName, Resolvable: true),
            new TypeRef(m.OutputType.FullName, Resolvable: true),
            new TypeRef(m.Service.FullName, Resolvable: true),
            MessageTemplateGenerator.GenerateJson(m.InputType),
            m.GetOptions()?.Deprecated == true);

    private static MessageDescription MapMessageDescription(MessageDescriptor g)
    {
        var fields = g.Fields.InDeclarationOrder()
            .Select(MapField)
            .ToList();

        var nested = g.NestedTypes
            .Where(n => n.GetOptions()?.MapEntry != true)
            .Select(n => new TypeRef(n.FullName, Resolvable: true))
            .Concat(g.EnumTypes.Select(e => new TypeRef(e.FullName, Resolvable: true)))
            .ToList();

        return new MessageDescription(
            g.FullName, g.Name, g.File.Name, fields, nested, MessageTemplateGenerator.GenerateJson(g),
            g.GetOptions()?.Deprecated == true);
    }

    private static FieldDescription MapField(FieldDescriptor field)
    {
        var (display, link) = DescribeFieldType(field);
        var label = field.IsMap
            ? FieldLabel.Map
            : field.IsRepeated ? FieldLabel.Repeated : FieldLabel.Optional;

        // proto3 `optional` produces a synthetic single-field oneof — not a user-facing oneof.
        var oneof = field.ContainingOneof is { IsSynthetic: false } o ? o.Name : null;

        return new FieldDescription(field.Name, field.FieldNumber, display, link, label, oneof, field.GetOptions()?.Deprecated == true);
    }

    private static EnumDescription MapEnumDescription(EnumDescriptor e)
        => new(
            e.FullName, e.Name, e.File.Name,
            e.Values.Select(v => new EnumValue(v.Name, v.Number, v.GetOptions()?.Deprecated == true)).ToList(),
            e.GetOptions()?.Deprecated == true);

    private static (string Display, TypeRef? Link) DescribeFieldType(FieldDescriptor field)
    {
        if (!field.IsMap)
        {
            return NamedType(field);
        }

        var entry = field.MessageType;
        var keyName = ScalarName(entry.Fields[1]);
        var (valueDisplay, valueLink) = NamedType(entry.Fields[2]);
        return ($"map<{keyName}, {valueDisplay}>", valueLink);
    }

    private static (string Display, TypeRef? Link) NamedType(FieldDescriptor field) => field.FieldType switch
    {
        FieldType.Message => ($".{field.MessageType.FullName}", new TypeRef(field.MessageType.FullName, Resolvable: true)),
        FieldType.Enum    => ($".{field.EnumType.FullName}", new TypeRef(field.EnumType.FullName, Resolvable: true)),
        _                 => (ScalarName(field), null)
    };

    private static string ScalarName(FieldDescriptor field) => field.FieldType switch
    {
        FieldType.Double   => "double",
        FieldType.Float    => "float",
        FieldType.Int64    => "int64",
        FieldType.UInt64   => "uint64",
        FieldType.Int32    => "int32",
        FieldType.Fixed64  => "fixed64",
        FieldType.Fixed32  => "fixed32",
        FieldType.Bool     => "bool",
        FieldType.String   => "string",
        FieldType.Bytes    => "bytes",
        FieldType.UInt32   => "uint32",
        FieldType.SFixed32 => "sfixed32",
        FieldType.SFixed64 => "sfixed64",
        FieldType.SInt32   => "sint32",
        FieldType.SInt64   => "sint64",
        FieldType.Enum     => $".{field.EnumType.FullName}",
        FieldType.Message  => $".{field.MessageType.FullName}",
        _                  => field.FieldType.ToString().ToLowerInvariant()
    };

    // --- Types branch (FR-022): all message/enum FQNs from the raw descriptor set ---

    private static List<TypeEntry> CollectTypes(FileDescriptorSet? set)
    {
        var types = new List<TypeEntry>();

        if (set is null)
        {
            return types;
        }

        foreach (var file in set.File)
        {
            var package = file.Package;

            foreach (var message in file.MessageType)
            {
                CollectMessage(message, package, package, types);
            }

            foreach (var enumType in file.EnumType)
            {
                types.Add(new TypeEntry(Combine(package, enumType.Name), TypeNodeKind.Enum, package, enumType.Options?.Deprecated == true));
            }
        }

        return types;
    }

    private static void CollectMessage(DescriptorProto message, string package, string scope, List<TypeEntry> types)
    {
        if (message.Options?.MapEntry == true)
        {
            return; // synthetic map-entry type
        }

        var fullName = Combine(scope, message.Name);
        types.Add(new TypeEntry(fullName, TypeNodeKind.Message, package, message.Options?.Deprecated == true));

        foreach (var nested in message.NestedType)
        {
            CollectMessage(nested, package, fullName, types);
        }

        foreach (var enumType in message.EnumType)
        {
            types.Add(new TypeEntry(Combine(fullName, enumType.Name), TypeNodeKind.Enum, package, enumType.Options?.Deprecated == true));
        }
    }

    private static string Combine(string scope, string name) => string.IsNullOrEmpty(scope) ? name : $"{scope}.{name}";

    private static DescriptorLoadError MapRpcError(RpcException ex)
    {
        if (ex.StatusCode == StatusCode.Unimplemented)
        {
            return new DescriptorLoadError(
                "The server does not implement gRPC server reflection.",
                "The server may not enable reflection; configure a protoset or .proto files instead.",
                ReflectionUnavailable: true);
        }

        var detail = string.IsNullOrWhiteSpace(ex.Status.Detail) ? ex.StatusCode.ToString() : ex.Status.Detail;
        return new DescriptorLoadError(detail, Hint: null, ReflectionUnavailable: false);
    }

    /// <summary>Collects Core's non-fatal descriptor warnings as data instead of console writes.</summary>
    private sealed class CollectingWarningSink : IDescriptorWarningSink
    {
        private readonly List<string> _messages = [];

        public IReadOnlyList<string> Messages => _messages;

        public void OnWarning(string message) => _messages.Add(message);
    }
}
