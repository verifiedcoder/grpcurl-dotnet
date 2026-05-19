namespace GrpCurl.Net.Utilities;

/// <summary>
///     Shared helpers for bounded local file reads.
/// </summary>
internal static class InputFileGuard
{
    public const long MaxGraphQlDocumentBytes = 4L * 1024 * 1024;
    public const long MaxGraphQlVariablesBytes = 4L * 1024 * 1024;
    public const long MaxMappingConfigBytes = 4L * 1024 * 1024;

    public static void EnsureFileSizeAtMost(string path, long maxBytes, string description)
    {
        var info = new FileInfo(path);

        if (!info.Exists)
        {
            return;
        }

        if (info.Length > maxBytes)
        {
            throw new InvalidDataException(
                $"{description} '{path}' is {info.Length:N0} bytes; maximum allowed is {maxBytes:N0} bytes.");
        }
    }

    public static async Task<string> ReadAllTextAsync(
        string path,
        long maxBytes,
        string description,
        CancellationToken cancellationToken)
    {
        EnsureFileSizeAtMost(path, maxBytes, description);

        return await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
    }

    public static async Task<byte[]> ReadAllBytesAsync(
        string path,
        long maxBytes,
        string description,
        CancellationToken cancellationToken)
    {
        EnsureFileSizeAtMost(path, maxBytes, description);

        return await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
    }
}
