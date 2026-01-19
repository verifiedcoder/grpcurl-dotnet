using Google.Protobuf.Reflection;
using Google.Protobuf.WellKnownTypes;

namespace GrpCurl.Net.DescriptorSources;

/// <summary>
///     Registry of well-known protobuf types that are built into Google.Protobuf.
///     Used as a fallback when server reflection doesn't include these common dependencies.
/// </summary>
public static class WellKnownTypeRegistry
{
    private static readonly Lazy<Dictionary<string, FileDescriptor>> LazyDescriptors = new(BuildRegistry);

    /// <summary>
    ///     Gets the dictionary mapping well-known proto file names to their FileDescriptors.
    /// </summary>
    public static IReadOnlyDictionary<string, FileDescriptor> Descriptors => LazyDescriptors.Value;

    /// <summary>
    ///     Attempts to get a FileDescriptor for a well-known proto file.
    /// </summary>
    /// <param name="fileName">The proto file name (e.g., "google/protobuf/timestamp.proto").</param>
    /// <param name="descriptor">The FileDescriptor if found, null otherwise.</param>
    /// <returns>True if the file is a well-known type and was found, false otherwise.</returns>
    public static bool TryGetDescriptor(string fileName, out FileDescriptor? descriptor)
    {
        return LazyDescriptors.Value.TryGetValue(fileName, out descriptor);
    }

    private static Dictionary<string, FileDescriptor> BuildRegistry()
    {
        var registry = new Dictionary<string, FileDescriptor>(StringComparer.Ordinal);

        // Core descriptor proto (contains FileDescriptorProto, etc.)
        // This is special - it's the "meta" proto that describes all protos
        // Access via reflection since DescriptorReflection.Descriptor is the descriptor.proto itself
        RegisterDescriptor(registry, DescriptorReflection.Descriptor);

        // Well-known types from Google.Protobuf.WellKnownTypes
        RegisterDescriptor(registry, Any.Descriptor.File);
        RegisterDescriptor(registry, Api.Descriptor.File);
        RegisterDescriptor(registry, Duration.Descriptor.File);
        RegisterDescriptor(registry, Empty.Descriptor.File);
        RegisterDescriptor(registry, FieldMask.Descriptor.File);
        RegisterDescriptor(registry, SourceContext.Descriptor.File);
        RegisterDescriptor(registry, Struct.Descriptor.File);
        RegisterDescriptor(registry, Timestamp.Descriptor.File);
        RegisterDescriptor(registry, Google.Protobuf.WellKnownTypes.Type.Descriptor.File);

        // Wrappers (DoubleValue, FloatValue, Int64Value, etc.)
        RegisterDescriptor(registry, DoubleValue.Descriptor.File);

        return registry;
    }

    private static void RegisterDescriptor(Dictionary<string, FileDescriptor> registry, FileDescriptor descriptor)
    {
        if (!registry.ContainsKey(descriptor.Name))
        {
            registry[descriptor.Name] = descriptor;

            // Also register all dependencies recursively
            foreach (var dependency in descriptor.Dependencies)
            {
                RegisterDescriptor(registry, dependency);
            }
        }
    }
}
