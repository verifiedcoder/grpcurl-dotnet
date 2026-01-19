using Google.Protobuf;
using Google.Protobuf.Reflection;

namespace GrpCurl.Net.DescriptorSources;

/// <summary>
///     Descriptor source that loads descriptors from compiled FileDescriptorSet files (protoset files).
///     These files are created using: protoc --descriptor_set_out=output.protoset --include_imports input.proto
/// </summary>
public sealed class ProtosetSource : IDescriptorSource
{
    private readonly Dictionary<string, FileDescriptor> _fileDescriptors = [];
    private readonly Dictionary<string, IDescriptor> _symbolCache = [];

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
    {
        var source = new ProtosetSource();

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
    {
        var source = new ProtosetSource();

        foreach (var filePath in filePaths)
        {
            await source.LoadProtosetAsync(filePath, cancellationToken);
        }

        return source;
    }

    private async Task LoadProtosetAsync(string filePath, CancellationToken cancellationToken)
    {
        var bytes = await File.ReadAllBytesAsync(filePath, cancellationToken);
        var fileDescriptorSet = FileDescriptorSet.Parser.ParseFrom(bytes);

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
                    Console.Error.WriteLine($"Warning: Proto file '{file.Name}' already loaded, skipping duplicate from '{filePath}'");
                }
                else
                {
                    FileDescriptorSet.File.Add(file);
                }
            }
        }

        // Build file descriptors from the set
        var unresolved = fileDescriptorSet.File.ToDictionary(f => f.Name, f => f);
        var resolved = new Dictionary<string, FileDescriptor>();

        foreach (var fileProto in fileDescriptorSet.File)
        {
            ResolveFileDescriptor(fileProto.Name, unresolved, resolved, []);
        }

        // Cache file descriptors, detecting conflicts
        foreach (var (name, descriptor) in resolved)
        {
            if (_fileDescriptors.ContainsKey(name))
            {
                Console.Error.WriteLine($"Warning: File descriptor '{name}' already cached, overwriting from '{filePath}'");
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
        HashSet<string> visitedInCurrentPath)
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

        if (!unresolved.TryGetValue(fileName, out var fileProto))
        {
            // Try well-known types as fallback - protosets may not include these
            if (WellKnownTypeRegistry.TryGetDescriptor(fileName, out var wellKnownDescriptor) && wellKnownDescriptor is not null)
            {
                resolved[fileName] = wellKnownDescriptor;

                return wellKnownDescriptor;
            }

            throw new InvalidOperationException($"File {fileName} not found in protoset");
        }

        // Track current file in resolution path
        visitedInCurrentPath.Add(fileName);

        try
        {
            // Resolve dependencies first
            var dependencies = fileProto.Dependency
                .Select(dependency => ResolveFileDescriptor(dependency, unresolved, resolved, visitedInCurrentPath))
                .ToList();

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
        if (_symbolCache.TryGetValue(fullName, out var existing))
        {
            // Only warn for service-level conflicts, not for common types like well-known types
            if (descriptor is ServiceDescriptor or MethodDescriptor)
            {
                Console.Error.WriteLine($"Warning: Symbol '{fullName}' already defined, overwriting from '{sourceFile}'");
            }
        }

        _symbolCache[fullName] = descriptor;
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
}