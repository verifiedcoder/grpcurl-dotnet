using Google.Protobuf;
using Google.Protobuf.Reflection;
using GrpCurl.Net.Utilities;

namespace GrpCurl.Net.DescriptorSources;

/// <summary>
///     Descriptor source that loads descriptors from compiled FileDescriptorSet files (protoset files).
///     These files are created using: protoc --descriptor_set_out=output.protoset --include_imports input.proto
/// </summary>
public sealed class ProtosetSource : IDescriptorSource
{
    private readonly Dictionary<string, FileDescriptor> _fileDescriptors = [];
    private readonly Dictionary<string, IDescriptor> _symbolCache = [];
    private readonly DescriptorSourceOptions _options;
    private readonly IDescriptorWarningSink _warningSink;

    private ProtosetSource(DescriptorSourceOptions? options = null, IDescriptorWarningSink? warningSink = null)
    {
        _options = options ?? DescriptorSourceOptions.Default;
        _options.ThrowIfInvalid();
        _warningSink = warningSink ?? ConsoleWarningSink.Instance;
    }

    /// <inheritdoc />
    public FileDescriptorSet? FileDescriptorSet { get; private set; }

    /// <inheritdoc />
    public Task<IReadOnlyList<string>> ListServicesAsync(CancellationToken cancellationToken = default)
    {
        var services = _fileDescriptors.Values
                                       .SelectMany(fd => fd.Services)
                                       .Select(s => s.FullName)
                                       .OrderBy(name => name)
                                       .ToList();

        return Task.FromResult<IReadOnlyList<string>>(services);
    }

    /// <inheritdoc />
    public Task<IDescriptor?> FindSymbolAsync(string fullyQualifiedName, CancellationToken cancellationToken = default)
    {
        _symbolCache.TryGetValue(fullyQualifiedName, out var descriptor);

        return Task.FromResult(descriptor);
    }

    /// <summary>
    ///     Loads a protoset file from the given path.
    /// </summary>
    /// <param name="filePath">Path to the protoset file.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A new ProtosetSource instance with the loaded descriptors.</returns>
    public static async Task<ProtosetSource> LoadFromFileAsync(string filePath, CancellationToken cancellationToken = default)
        => await LoadFromFileAsync(filePath, DescriptorSourceOptions.Default, cancellationToken).ConfigureAwait(false);

    internal static async Task<ProtosetSource> LoadFromFileAsync(
        string filePath,
        DescriptorSourceOptions options,
        CancellationToken cancellationToken = default,
        IDescriptorWarningSink? warningSink = null)
    {
        var source = new ProtosetSource(options, warningSink);

        await source.LoadProtosetAsync(filePath, cancellationToken);

        return source;
    }

    /// <summary>
    ///     Loads multiple protoset files.
    /// </summary>
    /// <param name="filePaths">Paths to the protoset files.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A new ProtosetSource instance with the loaded descriptors.</returns>
    public static async Task<ProtosetSource> LoadFromFilesAsync(IEnumerable<string> filePaths, CancellationToken cancellationToken = default)
        => await LoadFromFilesAsync(filePaths, DescriptorSourceOptions.Default, cancellationToken).ConfigureAwait(false);

    internal static async Task<ProtosetSource> LoadFromFilesAsync(
        IEnumerable<string> filePaths,
        DescriptorSourceOptions options,
        CancellationToken cancellationToken = default,
        IDescriptorWarningSink? warningSink = null)
    {
        var source = new ProtosetSource(options, warningSink);

        foreach (var filePath in filePaths)
        {
            await source.LoadProtosetAsync(filePath, cancellationToken);
        }

        return source;
    }

    private async Task LoadProtosetAsync(string filePath, CancellationToken cancellationToken)
    {
        var bytes = await InputFileGuard.ReadAllBytesAsync(
            filePath,
            _options.MaxProtosetFileBytes,
            "Protoset file",
            cancellationToken).ConfigureAwait(false);

        var fileDescriptorSet = FileDescriptorSet.Parser.ParseFrom(bytes);

        EnsureFileDescriptorCount(fileDescriptorSet.File.Count);

        // Merge into existing FileDescriptorSet instead of replacing
        if (FileDescriptorSet is null)
        {
            FileDescriptorSet = fileDescriptorSet;
        }
        else
        {
            // Merge file entries, detecting conflicts
            var existingFiles = FileDescriptorSet.File.Select(f => f.Name).ToHashSet();

            foreach (var file in fileDescriptorSet.File)
            {
                if (existingFiles.Contains(file.Name))
                {
                    _warningSink.OnWarning($"Warning: Proto file '{file.Name}' already loaded, skipping duplicate from '{filePath}'");
                }
                else
                {
                    FileDescriptorSet.File.Add(file);
                }
            }

            EnsureFileDescriptorCount(FileDescriptorSet.File.Count);
        }

        // Build file descriptors from the set
        var unresolved = fileDescriptorSet.File.ToDictionary(f => f.Name, f => f);
        var resolved = new Dictionary<string, FileDescriptor>();

        foreach (var fileProto in fileDescriptorSet.File)
        {
            ResolveFileDescriptor(fileProto.Name, unresolved, resolved, [], _options);
        }

        // Cache file descriptors, detecting conflicts
        foreach (var (name, descriptor) in resolved)
        {
            if (_fileDescriptors.ContainsKey(name))
            {
                _warningSink.OnWarning($"Warning: File descriptor '{name}' already cached, overwriting from '{filePath}'");
            }

            _fileDescriptors[name] = descriptor;
        }

        // Build symbol cache (will warn about conflicts)
        BuildSymbolCache(filePath);
    }

    private static FileDescriptor ResolveFileDescriptor(
        string fileName,
        Dictionary<string, FileDescriptorProto> unresolved,
        Dictionary<string, FileDescriptor> resolved,
        HashSet<string> visitedInCurrentPath,
        DescriptorSourceOptions options)
    {
        if (resolved.TryGetValue(fileName, out var existing))
        {
            return existing;
        }

        // Check for circular dependency
        if (visitedInCurrentPath.Contains(fileName))
        {
            var cycle = string.Join(" -> ", visitedInCurrentPath) + " -> " + fileName;

            throw new InvalidOperationException($"Circular dependency detected: {cycle}");
        }

        if (visitedInCurrentPath.Count >= options.MaxDependencyDepth)
        {
            throw new InvalidDataException(
                $"Descriptor dependency depth exceeded the maximum of {options.MaxDependencyDepth} while resolving '{fileName}'.");
        }

        if (!unresolved.TryGetValue(fileName, out var fileProto))
        {
            // Try well-known types as fallback - protosets may not include these
            if (!WellKnownTypeRegistry.TryGetDescriptor(fileName, out var wellKnownDescriptor) || wellKnownDescriptor is null)
            {
                throw new InvalidOperationException($"File {fileName} not found in protoset");
            }

            resolved[fileName] = wellKnownDescriptor;

            return wellKnownDescriptor;
        }

        // Track current file in resolution path
        visitedInCurrentPath.Add(fileName);

        try
        {
            // Resolve dependencies first
            var dependencies = fileProto.Dependency
                                        .Select(dependency => ResolveFileDescriptor(dependency, unresolved, resolved, visitedInCurrentPath, options))
                                        .ToList();

            // Collect ALL transitive dependency ByteStrings in dependency order
            // BuildFromByteStrings needs all dependencies present, not just direct ones
            var byteStrings = new List<ByteString>();
            var included = new HashSet<string>();

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
        }
        finally
        {
            // Remove from current path when done
            visitedInCurrentPath.Remove(fileName);
        }
    }

    private void BuildSymbolCache(string sourceFile)
    {
        foreach (var fileDescriptor in _fileDescriptors.Values)
        {
            // Cache services
            foreach (var service in fileDescriptor.Services)
            {
                CacheSymbolWithConflictCheck(service.FullName, service, sourceFile);

                // Cache methods
                foreach (var method in service.Methods)
                {
                    CacheSymbolWithConflictCheck(method.FullName, method, sourceFile);
                }
            }

            // Cache message types
            foreach (var messageType in fileDescriptor.MessageTypes)
            {
                CacheMessageTypeRecursive(messageType, sourceFile);
            }

            // Cache enums
            foreach (var enumType in fileDescriptor.EnumTypes)
            {
                CacheSymbolWithConflictCheck(enumType.FullName, enumType, sourceFile);

                foreach (var value in enumType.Values)
                {
                    CacheSymbolWithConflictCheck(value.FullName, value, sourceFile);
                }
            }
        }
    }

    private void CacheSymbolWithConflictCheck(string fullName, IDescriptor descriptor, string sourceFile)
    {
        // Only warn for service-level conflicts, not for common types like well-known types
        if (_symbolCache.TryGetValue(fullName, out _) && descriptor is ServiceDescriptor or MethodDescriptor)
        {
            _warningSink.OnWarning($"Warning: Symbol '{fullName}' already defined, overwriting from '{sourceFile}'");
        }

        _symbolCache[fullName] = descriptor;

        if (_symbolCache.Count > _options.MaxSymbols)
        {
            throw new InvalidDataException(
                $"Descriptor symbol count exceeded the maximum of {_options.MaxSymbols} while loading '{sourceFile}'.");
        }
    }

    private void CacheMessageTypeRecursive(MessageDescriptor messageType, string sourceFile)
    {
        CacheSymbolWithConflictCheck(messageType.FullName, messageType, sourceFile);

        // Cache nested types
        foreach (var nested in messageType.NestedTypes)
        {
            CacheMessageTypeRecursive(nested, sourceFile);
        }

        // Cache nested enums
        foreach (var enumType in messageType.EnumTypes)
        {
            CacheSymbolWithConflictCheck(enumType.FullName, enumType, sourceFile);

            foreach (var value in enumType.Values)
            {
                CacheSymbolWithConflictCheck(value.FullName, value, sourceFile);
            }
        }

        // Cache fields
        foreach (var field in messageType.Fields.InDeclarationOrder())
        {
            CacheSymbolWithConflictCheck(field.FullName, field, sourceFile);
        }

        // Cache oneofs
        foreach (var oneof in messageType.Oneofs)
        {
            CacheSymbolWithConflictCheck(oneof.FullName, oneof, sourceFile);
        }
    }

    private void EnsureFileDescriptorCount(int count)
    {
        if (count > _options.MaxFileDescriptors)
        {
            throw new InvalidDataException(
                $"File descriptor count {count:N0} exceeds the maximum of {_options.MaxFileDescriptors:N0}.");
        }
    }
}
