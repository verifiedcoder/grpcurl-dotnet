using GrpCurl.Net.Studio.ViewModels.Models.Connections;

namespace GrpCurl.Net.Studio.ViewModels.Services;

/// <summary>
///     Loads and persists workspaces (SPEC-040). A workspace is a <c>.gcnws.json</c> document tracked by
///     absolute path, not a registry entry: <see cref="OpenAsync" /> reads one from anywhere (strictly —
///     a corrupt/newer file surfaces as <see cref="WorkspaceSchemaException" />), <see cref="SaveAsAsync" />
///     writes one to a chosen path, and the most-recent paths are remembered (<see cref="RecentWorkspaces" />).
///     <see cref="SaveAsync" /> persists mutations to the active file (<see cref="CurrentPath" />).
/// </summary>
public interface IWorkspaceStore
{
    /// <summary>The workspace currently loaded in memory.</summary>
    WorkspaceModel Current { get; }

    /// <summary>The absolute path backing <see cref="Current" /> (the default workspace file at startup).</summary>
    string? CurrentPath { get; }

    /// <summary>Recently opened/saved workspaces, newest first, with dangling entries flagged.</summary>
    IReadOnlyList<RecentWorkspace> RecentWorkspaces { get; }

    /// <summary>Loads the default startup workspace (resilient: a corrupt/newer file is set aside).</summary>
    Task<WorkspaceModel> LoadAsync(CancellationToken cancellationToken = default);

    /// <summary>
    ///     Opens a workspace document from <paramref name="path" /> strictly: a corrupt or newer file throws
    ///     <see cref="WorkspaceSchemaException" /> and leaves the current workspace untouched. On success it
    ///     becomes <see cref="Current" />, <see cref="CurrentPath" /> points at it, and it heads the recents.
    /// </summary>
    Task<WorkspaceModel> OpenAsync(string path, CancellationToken cancellationToken = default);

    /// <summary>Persists <paramref name="workspace" /> to the active <see cref="CurrentPath" />.</summary>
    Task SaveAsync(WorkspaceModel workspace, CancellationToken cancellationToken = default);

    /// <summary>Writes <paramref name="workspace" /> to <paramref name="path" />, which becomes the active file + a recent.</summary>
    Task SaveAsAsync(WorkspaceModel workspace, string path, CancellationToken cancellationToken = default);

    /// <summary>Replaces <see cref="Current" /> with a fresh empty workspace (a new identity, no saved path yet).</summary>
    WorkspaceModel NewWorkspace();

    /// <summary>Removes <paramref name="path" /> from the recents (e.g. a dangling entry the user forgets).</summary>
    Task RemoveRecentAsync(string path, CancellationToken cancellationToken = default);
}
