using GrpCurl.Net.Studio.ViewModels.Models.Connections;

namespace GrpCurl.Net.Studio.ViewModels.Services;

/// <summary>
///     Manages the workspace's saved requests (FR-145). Saved requests are workspace-level, so this sits over
///     <see cref="IWorkspaceStore" /> and persists without disturbing the connection, profile, or environment
///     lists. The sidebar groups them by <see cref="SavedRequest.ConnectionId" /> and opens them into tabs.
/// </summary>
public interface ISavedRequestStore
{
    /// <summary>All saved requests in the live workspace.</summary>
    IReadOnlyList<SavedRequest> Requests { get; }

    /// <summary>The saved requests bound to a given connection (sidebar grouping, FR-145).</summary>
    IReadOnlyList<SavedRequest> ForConnection(string connectionId);

    /// <summary>Raised after a save or delete, so the sidebar can refresh its nested lists.</summary>
    event EventHandler? Changed;

    /// <summary>
    ///     Inserts a new saved request or replaces the existing one with the same <see cref="SavedRequest.Id" />,
    ///     preserving the rest of the workspace.
    /// </summary>
    Task SaveAsync(SavedRequest request, CancellationToken cancellationToken = default);

    /// <summary>Removes the saved request with this id. A no-op if the id is unknown.</summary>
    Task DeleteAsync(string requestId, CancellationToken cancellationToken = default);
}
