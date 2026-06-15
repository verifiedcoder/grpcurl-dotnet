namespace GrpCurl.Net.Studio.ViewModels.Models.Connections;

/// <summary>
///     The persisted workspace (SPEC-040 §3.2). Phase 1 stores a single default workspace
///     holding the connection list; E3.1 extends this to multiple named workspaces opened from
///     arbitrary paths.
/// </summary>
public sealed class WorkspaceModel
{
    public int SchemaVersion { get; set; } = 1;

    public List<SavedConnection> Connections { get; set; } = [];

    public static WorkspaceModel Empty() => new();
}
