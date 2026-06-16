namespace GrpCurl.Net.Studio.ViewModels.Models;

/// <summary>
///     Result of a protoc probe (FR-154 Detect/Verify): whether a usable <c>protoc</c> was found, its
///     resolved path and version, and a human-readable message for the settings screen.
/// </summary>
public sealed record ProtocInfo(bool Found, string? Path, string? Version, string Message)
{
    public static ProtocInfo Ok(string path, string version)
        => new(true, path, version, $"{version} — {path}");

    public static ProtocInfo NotFound(string message) => new(false, null, null, message);
}
