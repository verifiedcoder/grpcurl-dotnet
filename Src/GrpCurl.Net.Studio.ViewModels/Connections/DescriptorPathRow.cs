namespace GrpCurl.Net.Studio.ViewModels.Connections;

/// <summary>
///     One ordered path row in the descriptor-source editor (a protoset, a <c>.proto</c> file, or an
///     import directory). For protosets it carries the on-disk size and an over-limit flag computed
///     <em>before</em> load against Core's 64&#160;MiB protoset cap (FR-041/FR-047); a missing file is
///     flagged too (SEC-016 — paths are referenced, not copied).
/// </summary>
public sealed record DescriptorPathRow(string Path)
{
    /// <summary>Core's <c>DescriptorSourceOptions.MaxProtosetFileBytes</c> default (64 MiB).</summary>
    public const long ProtosetByteCap = 64L * 1024 * 1024;

    /// <summary>On-disk size in bytes, or null when the path is not an existing file (e.g. an import dir).</summary>
    public long? SizeBytes { get; init; }

    /// <summary>True when this row is a missing file path (shown as a warning before load).</summary>
    public bool Missing { get; init; }

    public string Display => Path;

    public string? SizeText => SizeBytes is { } bytes ? FormatSize(bytes) : null;

    public bool IsOverLimit => SizeBytes is { } bytes && bytes > ProtosetByteCap;

    public string? Warning => Missing
        ? "File not found"
        : IsOverLimit ? "Exceeds the 64 MiB protoset limit" : null;

    public bool HasSize => SizeText is not null;

    public bool HasWarning => Warning is not null;

    /// <summary>Builds a protoset row, probing the file size and existence on disk.</summary>
    public static DescriptorPathRow ForProtoset(string path)
    {
        try
        {
            var info = new FileInfo(path);
            return info.Exists
                ? new DescriptorPathRow(path) { SizeBytes = info.Length }
                : new DescriptorPathRow(path) { Missing = true };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return new DescriptorPathRow(path) { Missing = true };
        }
    }

    /// <summary>Builds a proto-file row, flagging a missing file but not measuring size.</summary>
    public static DescriptorPathRow ForProtoFile(string path)
        => new(path) { Missing = !SafeExists(path) };

    /// <summary>Builds an import-directory row (no size; not flagged when absent — dirs are advisory to protoc).</summary>
    public static DescriptorPathRow ForImportPath(string path) => new(path);

    private static bool SafeExists(string path)
    {
        try
        {
            return File.Exists(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return false;
        }
    }

    private static string FormatSize(long bytes) => bytes switch
    {
        >= 1024 * 1024 => $"{bytes / (1024.0 * 1024.0):0.#} MiB",
        >= 1024 => $"{bytes / 1024.0:0.#} KiB",
        _ => $"{bytes} B"
    };
}
