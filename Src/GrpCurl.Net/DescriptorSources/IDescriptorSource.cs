using Google.Protobuf.Reflection;

namespace GrpCurl.Net.DescriptorSources;

/// <summary>
///     Provides access to protobuf descriptors for gRPC services.
///     Can be backed by server reflection, proto files, or protoset files.
/// </summary>
public interface IDescriptorSource
{
    /// <summary>
    ///     Gets the file descriptor set containing all loaded descriptors.
    /// </summary>
    FileDescriptorSet? FileDescriptorSet { get; }

    /// <summary>
    ///     Lists all available service names.
    /// </summary>
    /// <returns>Fully-qualified service names</returns>
    Task<IReadOnlyList<string>> ListServicesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    ///     Finds a descriptor for the given fully-qualified symbol name.
    /// </summary>
    /// <param name="fullyQualifiedName">The fully-qualified name (e.g., "my.package.Service")</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The descriptor for the symbol, or null if not found</returns>
    Task<IDescriptor?> FindSymbolAsync(string fullyQualifiedName, CancellationToken cancellationToken = default);
}