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

    /// <summary>
    ///     True when the in-memory workspace has changes not yet on disk — the window between a mutation
    ///     (the autosave-debounced <see cref="SaveAsync" />) and its flush, or any change to an untitled
    ///     workspace that has nowhere to autosave yet (<see cref="CurrentPath" /> is null).
    /// </summary>
    bool IsDirty { get; }

    /// <summary>Raised whenever <see cref="IsDirty" /> changes.</summary>
    event EventHandler? DirtyChanged;

    /// <summary>
    ///     FR-148: true when the active workspace file is read-only on disk. Autosave and explicit Save are
    ///     suppressed (changes stay in memory, dirty); the shell shows a banner and offers Save As instead.
    ///     False for an untitled workspace (nothing on disk yet) and after a successful Save As to a writable path.
    /// </summary>
    bool IsCurrentReadOnly { get; }

    /// <summary>Raised whenever <see cref="IsCurrentReadOnly" /> changes (open / load / new / save-as / reload).</summary>
    event EventHandler? ReadOnlyChanged;

    /// <summary>Loads the default startup workspace (resilient: a corrupt/newer file is set aside).</summary>
    Task<WorkspaceModel> LoadAsync(CancellationToken cancellationToken = default);

    /// <summary>
    ///     Opens a workspace document from <paramref name="path" /> strictly: a corrupt or newer file throws
    ///     <see cref="WorkspaceSchemaException" /> and leaves the current workspace untouched. On success it
    ///     becomes <see cref="Current" />, <see cref="CurrentPath" /> points at it, and it heads the recents.
    /// </summary>
    Task<WorkspaceModel> OpenAsync(string path, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Applies a mutation: replaces <see cref="Current" /> with <paramref name="workspace" /> and
    ///     autosaves it to <see cref="CurrentPath" /> (debounced; immediate when the debounce is zero).
    ///     An untitled workspace stays dirty until <see cref="SaveAsAsync" /> gives it a path.
    /// </summary>
    Task SaveAsync(WorkspaceModel workspace, CancellationToken cancellationToken = default);

    /// <summary>Flushes any pending autosave to disk immediately and clears <see cref="IsDirty" /> (explicit Save).</summary>
    Task SaveNowAsync(CancellationToken cancellationToken = default);

    /// <summary>
    ///     Re-reads <see cref="CurrentPath" /> from disk, discarding unsaved in-memory changes (Reload from
    ///     disk; the caller confirms first when dirty). A corrupt/newer file on disk throws
    ///     <see cref="WorkspaceSchemaException" />. A no-op for an untitled workspace.
    /// </summary>
    Task ReloadAsync(CancellationToken cancellationToken = default);

    /// <summary>Writes <paramref name="workspace" /> to <paramref name="path" />, which becomes the active file + a recent.</summary>
    Task SaveAsAsync(WorkspaceModel workspace, string path, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Writes a copy of <paramref name="workspace" /> to <paramref name="path" /> for sharing (FR-164)
    ///     without changing the active file, dirty state, or recents. The format is already secret-free
    ///     (FR-141), so an export needs no extra sanitisation.
    /// </summary>
    Task ExportAsync(WorkspaceModel workspace, string path, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Reads and deserializes a workspace document from <paramref name="path" /> strictly (a corrupt/newer
    ///     file throws <see cref="WorkspaceSchemaException" />) <em>without</em> opening it — the active
    ///     workspace, path, and recents are untouched. Used to preview a file before merging it (FR-164).
    /// </summary>
    Task<WorkspaceModel> ReadAsync(string path, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Replaces <see cref="Current" /> with a fresh workspace (a new identity, no saved path yet). When
    ///     <paramref name="withStarterConnection" /> is set, it is seeded with the FR-149 example connection.
    /// </summary>
    WorkspaceModel NewWorkspace(bool withStarterConnection = false);

    /// <summary>Removes <paramref name="path" /> from the recents (e.g. a dangling entry the user forgets).</summary>
    Task RemoveRecentAsync(string path, CancellationToken cancellationToken = default);
}
