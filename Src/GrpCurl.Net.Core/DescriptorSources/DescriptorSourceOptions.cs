namespace GrpCurl.Net.DescriptorSources;

/// <summary>
///     Resource limits applied while loading protobuf descriptors from local protosets,
///     generated protosets, or server reflection responses.
/// </summary>
public sealed record DescriptorSourceOptions
{
    /// <summary>
    ///     Default maximum size, in bytes, for a single local protoset file.
    /// </summary>
    public const long DefaultMaxProtosetFileBytes = 64L * 1024 * 1024;

    /// <summary>
    ///     Default maximum aggregate descriptor payload size, in bytes, accepted from one reflection response.
    /// </summary>
    public const long DefaultMaxReflectionDescriptorBytes = 16L * 1024 * 1024;

    /// <summary>
    ///     Default maximum number of file descriptors retained by a descriptor source.
    /// </summary>
    public const int DefaultMaxFileDescriptors = 2048;

    /// <summary>
    ///     Default maximum import dependency depth while resolving descriptor graphs.
    /// </summary>
    public const int DefaultMaxDependencyDepth = 128;

    /// <summary>
    ///     Default maximum number of protobuf symbols cached by a descriptor source.
    /// </summary>
    public const int DefaultMaxSymbols = 65536;

    /// <summary>
    ///     Gets the default descriptor resource limits.
    /// </summary>
    public static DescriptorSourceOptions Default { get; } = new();

    /// <summary>
    ///     Gets the maximum size, in bytes, accepted for a single local protoset file.
    /// </summary>
    public long MaxProtosetFileBytes { get; init; } = DefaultMaxProtosetFileBytes;

    /// <summary>
    ///     Gets the maximum aggregate descriptor payload size, in bytes, accepted from one reflection response.
    /// </summary>
    public long MaxReflectionDescriptorBytes { get; init; } = DefaultMaxReflectionDescriptorBytes;

    /// <summary>
    ///     Gets the maximum number of file descriptors retained by a descriptor source.
    /// </summary>
    public int MaxFileDescriptors { get; init; } = DefaultMaxFileDescriptors;

    /// <summary>
    ///     Gets the maximum import dependency depth while resolving descriptor graphs.
    /// </summary>
    public int MaxDependencyDepth { get; init; } = DefaultMaxDependencyDepth;

    /// <summary>
    ///     Gets the maximum number of protobuf symbols cached by a descriptor source.
    /// </summary>
    public int MaxSymbols { get; init; } = DefaultMaxSymbols;

    internal void ThrowIfInvalid()
    {
        if (MaxProtosetFileBytes <= 0)
        {
            throw new InvalidOperationException("Maximum protoset file size must be positive.");
        }

        if (MaxReflectionDescriptorBytes <= 0)
        {
            throw new InvalidOperationException("Maximum reflection descriptor size must be positive.");
        }

        if (MaxFileDescriptors <= 0)
        {
            throw new InvalidOperationException("Maximum file descriptor count must be positive.");
        }

        if (MaxDependencyDepth <= 0)
        {
            throw new InvalidOperationException("Maximum dependency depth must be positive.");
        }

        if (MaxSymbols <= 0)
        {
            throw new InvalidOperationException("Maximum symbol count must be positive.");
        }
    }
}
