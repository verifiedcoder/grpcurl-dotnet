using Google.Protobuf;
using Google.Protobuf.Reflection;
using Grpc.Core;
using Grpc.Net.Client;
using Grpc.Reflection.V1Alpha;
using System.Collections.Concurrent;

namespace GrpCurl.Net.DescriptorSources;

/// <summary>
///     Descriptor source that uses gRPC server reflection to dynamically discover service schemas.
///     Requires the server to have the grpc.reflection.v1alpha.ServerReflection service enabled.
/// </summary>
/// <param name="channel">The gRPC channel to use for server reflection.</param>
/// <param name="metadata">Optional metadata to send with reflection requests.</param>
/// <param name="ownsChannel">If true, the channel will be disposed when this object is disposed. Default is false.</param>
public sealed class ReflectionSource(GrpcChannel channel, Metadata? metadata = null, bool ownsChannel = false)
    : IDescriptorSource, IDisposable
{
    private readonly GrpcChannel _channel = channel ?? throw new ArgumentNullException(nameof(channel));
    private readonly ServerReflection.ServerReflectionClient _client = new(channel);
    private readonly ConcurrentDictionary<string, FileDescriptor> _fileDescriptors = new();
    private readonly ConcurrentDictionary<string, IDescriptor> _symbolCache = new();
    private bool _servicesLoaded;

    /// <summary>
    ///     Gets the FileDescriptorSet containing all discovered file descriptors.
    ///     Built dynamically from cached FileDescriptors for protoset export.
    /// </summary>
    public FileDescriptorSet FileDescriptorSet
    {
        get
        {
            var fds = new FileDescriptorSet();

            foreach (var fd in _fileDescriptors.Values)
            {
                // Parse FileDescriptorProto from serialized data
                var fileProto = FileDescriptorProto.Parser.ParseFrom(fd.SerializedData);

                fds.File.Add(fileProto);
            }

            return fds;
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<string>> ListServicesAsync(CancellationToken cancellationToken = default)
    {
        if (!_servicesLoaded)
        {
            await LoadServicesAsync(cancellationToken);
        }

        return
        [
            .. _symbolCache.Values
                .OfType<ServiceDescriptor>()
                .Select(s => s.FullName)
                .OrderBy(name => name)
        ];
    }

    /// <inheritdoc />
    public async Task<IDescriptor?> FindSymbolAsync(string fullyQualifiedName, CancellationToken cancellationToken = default)
    {
        if (_symbolCache.TryGetValue(fullyQualifiedName, out var cached))
        {
            return cached;
        }

        // Try to load the symbol from the server
        try
        {
            var fileDescriptor = await LoadSymbolAsync(fullyQualifiedName, cancellationToken);

            if (fileDescriptor is not null)
            {
                CacheFileDescriptor(fileDescriptor);

                _symbolCache.TryGetValue(fullyQualifiedName, out var descriptor);

                return descriptor;
            }
        }
        catch (RpcException ex) when (ex.StatusCode == StatusCode.Unimplemented)
        {
            throw new NotSupportedException("Server does not support reflection", ex);
        }

        return null;
    }

    /// <summary>
    ///     Creates a new ReflectionSource for the specified server address.
    ///     This is a convenience factory for simple use cases where you don't need
    ///     fine-grained control over the gRPC channel configuration.
    /// </summary>
    /// <remarks>
    ///     The returned instance owns its channel and will dispose it when
    ///     <see cref="Dispose"/> is called. For advanced scenarios requiring
    ///     TLS configuration, custom headers, or channel reuse, create the
    ///     <see cref="GrpcChannel"/> separately and use the constructor instead.
    /// </remarks>
    /// <param name="address">The gRPC server address (e.g., "https://localhost:50051").</param>
    /// <param name="options">Optional channel options for configuring the underlying HTTP handler.</param>
    /// <returns>A new ReflectionSource instance that owns and manages its channel lifecycle.</returns>
    /// <example>
    ///     <code>
    ///     using var source = ReflectionSource.Create("https://localhost:50051");
    ///     var services = await source.ListServicesAsync();
    ///     </code>
    /// </example>
    // ReSharper disable once UnusedMember.Global as intentionally made available in public contract
    public static ReflectionSource Create(string address, GrpcChannelOptions? options = null)
    {
        var channel = GrpcChannel.ForAddress(address, options ?? new GrpcChannelOptions());

        return new ReflectionSource(channel, null, true);
    }

    /// <summary>
    ///     Disposes the channel, if this instance owns it.
    /// </summary>
    public void Dispose()
    {
        if (ownsChannel)
        {
            _channel.Dispose();
        }
    }

    private async Task LoadServicesAsync(CancellationToken cancellationToken)
    {
        // Check cancellation before starting stream to avoid server-side exceptions
        cancellationToken.ThrowIfCancellationRequested();

        var callOptions = new CallOptions(metadata, cancellationToken: cancellationToken);

        using var call = _client.ServerReflectionInfo(callOptions);

        // Request list of services
        await call.RequestStream.WriteAsync(new ServerReflectionRequest
        {
            ListServices = ""
        }, cancellationToken);

        // Read response
        if (!await call.ResponseStream.MoveNext(cancellationToken))
        {
            throw new InvalidOperationException("No response from reflection service");
        }

        var response = call.ResponseStream.Current;

        if (response.MessageResponseCase != ServerReflectionResponse.MessageResponseOneofCase.ListServicesResponse)
        {
            throw new InvalidOperationException($"Unexpected response: {response.MessageResponseCase}");
        }

        // Load file descriptors for each service
        var failedServices = new List<string>();

        foreach (var service in response.ListServicesResponse.Service)
        {
            try
            {
                await LoadSymbolAsync(service.Name, cancellationToken);
            }
            catch (Exception ex)
            {
                // Log warning and continue - some services may not be fully resolvable
                failedServices.Add($"{service.Name}: {ex.Message}");
            }
        }

        if (failedServices.Count > 0)
        {
            Console.Error.WriteLine($"Warning: Failed to load {failedServices.Count} service(s):");

            foreach (var failure in failedServices)
            {
                Console.Error.WriteLine($"  - {failure}");
            }
        }

        _servicesLoaded = true;

        // Signal that we're done sending requests
        await call.RequestStream.CompleteAsync();

        // Drain the response stream to allow server to complete gracefully
        while (await call.ResponseStream.MoveNext(cancellationToken))
        {
            // Discard any remaining responses
        }
    }

    private async Task<FileDescriptor?> LoadSymbolAsync(string fullyQualifiedName, CancellationToken cancellationToken)
    {
        // Check cancellation before starting stream to avoid server-side exceptions
        cancellationToken.ThrowIfCancellationRequested();

        var callOptions = new CallOptions(metadata, cancellationToken: cancellationToken);

        using var call = _client.ServerReflectionInfo(callOptions);

        // Request file containing symbol
        await call.RequestStream.WriteAsync(new ServerReflectionRequest
        {
            FileContainingSymbol = fullyQualifiedName
        }, cancellationToken);

        if (!await call.ResponseStream.MoveNext(cancellationToken))
        {
            return null;
        }

        var response = call.ResponseStream.Current;

        if (response.MessageResponseCase == ServerReflectionResponse.MessageResponseOneofCase.ErrorResponse)
        {
            // Symbol not found
            return null;
        }

        if (response.MessageResponseCase != ServerReflectionResponse.MessageResponseOneofCase.FileDescriptorResponse)
        {
            throw new InvalidOperationException($"Unexpected response: {response.MessageResponseCase}");
        }

        // Process file descriptors (includes dependencies)
        var unresolved = new Dictionary<string, FileDescriptorProto>();

        foreach (var fileDescriptorBytes in response.FileDescriptorResponse.FileDescriptorProto)
        {
            var fileProto = FileDescriptorProto.Parser.ParseFrom(fileDescriptorBytes);

            unresolved[fileProto.Name] = fileProto;
        }

        // Resolve all file descriptors
        var resolved = new Dictionary<string, FileDescriptor>();

        foreach (var fileProto in unresolved.Values.Where(fileProto => !_fileDescriptors.ContainsKey(fileProto.Name)))
        {
            ResolveFileDescriptor(fileProto.Name, unresolved, resolved);
        }

        // Cache newly resolved descriptors
        foreach (var (name, descriptor) in resolved)
        {
            if (!_fileDescriptors.ContainsKey(name))
            {
                CacheFileDescriptor(descriptor);
            }
        }

        // Signal that we're done sending requests
        await call.RequestStream.CompleteAsync();

        // Drain the response stream to allow server to complete gracefully
        while (await call.ResponseStream.MoveNext(cancellationToken))
        {
            // Discard any remaining responses
        }

        return resolved.Values.FirstOrDefault();
    }

    private FileDescriptor ResolveFileDescriptor(
        string fileName,
        Dictionary<string, FileDescriptorProto> unresolved,
        Dictionary<string, FileDescriptor> resolved)
    {
        // Check if already resolved in this session
        if (resolved.TryGetValue(fileName, out var existing))
        {
            return existing;
        }

        // Check if already cached from previous calls
        if (_fileDescriptors.TryGetValue(fileName, out var cached))
        {
            resolved[fileName] = cached;

            return cached;
        }

        if (!unresolved.TryGetValue(fileName, out var fileProto))
        {
            // Try well-known types as fallback - server reflection often doesn't include these
            if (WellKnownTypeRegistry.TryGetDescriptor(fileName, out var wellKnownDescriptor) && wellKnownDescriptor is not null)
            {
                resolved[fileName] = wellKnownDescriptor;

                return wellKnownDescriptor;
            }

            throw new InvalidOperationException($"File {fileName} not found");
        }

        // Resolve dependencies first
        var dependencies = fileProto.Dependency.Select(dependency => ResolveFileDescriptor(dependency, unresolved, resolved)).ToList();

        // Collect ALL transitive dependency ByteStrings in dependency order
        // BuildFromByteStrings needs all dependencies present, not just direct ones
        var byteStrings = new List<ByteString>();
        var included = new HashSet<string>();

        void AddDependencyBytes(FileDescriptor dep)
        {
            if (included.Contains(dep.Name))
            {
                return;
            }

            // Add transitive dependencies first (depth-first)
            foreach (var transitiveDep in dep.Dependencies)
            {
                AddDependencyBytes(transitiveDep);
            }

            // Then add this dependency
            byteStrings.Add(dep.SerializedData);
            included.Add(dep.Name);
        }

        foreach (var dep in dependencies)
        {
            AddDependencyBytes(dep);
        }

        // Add current file bytes
        using (var stream = new MemoryStream())
        {
            using (var output = new CodedOutputStream(stream, true))
            {
                fileProto.WriteTo(output);
            }

            byteStrings.Add(ByteString.CopyFrom(stream.ToArray()));
        }

        // BuildFromByteStrings returns all descriptors; we want the last one (our file)
        var results = FileDescriptor.BuildFromByteStrings(byteStrings);
        var fileDescriptor = results[^1];

        resolved[fileName] = fileDescriptor;

        return fileDescriptor;
    }

    private void CacheFileDescriptor(FileDescriptor fileDescriptor)
    {
        _fileDescriptors[fileDescriptor.Name] = fileDescriptor;

        // Cache services
        foreach (var service in fileDescriptor.Services)
        {
            _symbolCache[service.FullName] = service;

            // Cache methods
            foreach (var method in service.Methods)
            {
                _symbolCache[method.FullName] = method;
            }
        }

        // Cache message types
        foreach (var messageType in fileDescriptor.MessageTypes)
        {
            CacheMessageTypeRecursive(messageType);
        }

        // Cache enums
        foreach (var enumType in fileDescriptor.EnumTypes)
        {
            _symbolCache[enumType.FullName] = enumType;

            foreach (var value in enumType.Values)
            {
                _symbolCache[value.FullName] = value;
            }
        }
    }

    private void CacheMessageTypeRecursive(MessageDescriptor messageType)
    {
        _symbolCache[messageType.FullName] = messageType;

        foreach (var nested in messageType.NestedTypes)
        {
            CacheMessageTypeRecursive(nested);
        }

        foreach (var enumType in messageType.EnumTypes)
        {
            _symbolCache[enumType.FullName] = enumType;

            foreach (var value in enumType.Values)
            {
                _symbolCache[value.FullName] = value;
            }
        }

        foreach (var field in messageType.Fields.InDeclarationOrder())
        {
            _symbolCache[field.FullName] = field;
        }

        foreach (var oneof in messageType.Oneofs)
        {
            _symbolCache[oneof.FullName] = oneof;
        }
    }
}