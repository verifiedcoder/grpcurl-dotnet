using GrpCurl.Net.Studio.ViewModels.Models.Connections;
using GrpCurl.Net.Studio.ViewModels.Services;

namespace GrpCurl.Net.Studio.Services;

/// <summary>
///     Default <see cref="ISavedRequestStore" />. Reads from and writes to the live
///     <see cref="IWorkspaceStore.Current" />, cloning the workspace so a saved-request save never drops the
///     connection, TLS-profile, or environment lists (the same hazard the other workspace-level stores
///     guard against). Saved requests carry no secret values, so there is nothing to purge on delete.
/// </summary>
internal sealed class SavedRequestStore(IWorkspaceStore workspace) : ISavedRequestStore
{
    public IReadOnlyList<SavedRequest> Requests => workspace.Current.SavedRequests;

    public IReadOnlyList<SavedRequest> ForConnection(string connectionId)
        => workspace.Current.SavedRequests.Where(r => r.ConnectionId == connectionId).ToList();

    public Task SaveAsync(SavedRequest request, CancellationToken cancellationToken = default)
    {
        var next = workspace.Current.Copy();
        next.SavedRequests = next.SavedRequests.Where(r => r.Id != request.Id).Append(request).ToList();

        return workspace.SaveAsync(next, cancellationToken);
    }

    public Task DeleteAsync(string requestId, CancellationToken cancellationToken = default)
    {
        if (workspace.Current.SavedRequests.All(r => r.Id != requestId))
        {
            return Task.CompletedTask;
        }

        var next = workspace.Current.Copy();
        next.SavedRequests = next.SavedRequests.Where(r => r.Id != requestId).ToList();

        return workspace.SaveAsync(next, cancellationToken);
    }
}
