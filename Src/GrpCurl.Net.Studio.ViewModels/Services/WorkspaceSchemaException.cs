namespace GrpCurl.Net.Studio.ViewModels.Services;

/// <summary>
///     A workspace file cannot be loaded as-is (SPEC-040 §"versioning"): it is either newer than this
///     build understands or structurally corrupt. <see cref="Message" /> is user-facing — the open flow
///     shows it verbatim; Studio never partial-loads, auto-repairs, or strips a newer file.
/// </summary>
public sealed class WorkspaceSchemaException : Exception
{
    private WorkspaceSchemaException(string message, bool isNewerVersion) : base(message)
        => IsNewerVersion = isNewerVersion;

    /// <summary>True when the file's schema is newer than this build (vs. corrupt/unreadable).</summary>
    public bool IsNewerVersion { get; }

    public static WorkspaceSchemaException NewerVersion(int fileVersion, int maxSupported) => new(
        $"This workspace was created by a newer version of Studio (schema v{fileVersion}; "
        + $"this Studio reads up to v{maxSupported}). Update Studio to open it.",
        isNewerVersion: true);

    public static WorkspaceSchemaException Corrupt(string detail) => new(
        $"This workspace file is not valid and cannot be opened: {detail}",
        isNewerVersion: false);
}
