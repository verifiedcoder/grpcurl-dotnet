using GrpCurl.Net.Studio.ViewModels.Models.Session;

namespace GrpCurl.Net.Studio.ViewModels.Services;

/// <summary>
///     Persists the per-machine UI session (open tabs + active tab) to local storage, separate from the
///     workspace file (FR-141/146). Reads are tolerant: a missing or corrupt file yields an empty session.
/// </summary>
public interface ISessionStore
{
    Task<SessionState> LoadAsync(CancellationToken cancellationToken = default);

    Task SaveAsync(SessionState state, CancellationToken cancellationToken = default);
}
