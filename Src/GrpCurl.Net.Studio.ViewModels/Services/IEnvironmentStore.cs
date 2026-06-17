using GrpCurl.Net.Studio.ViewModels.Models.Connections;

namespace GrpCurl.Net.Studio.ViewModels.Services;

/// <summary>
///     Manages the workspace's named environments (FR-130). Environments are workspace-level, so this sits
///     over <see cref="IWorkspaceStore" /> and persists without disturbing the connection or TLS-profile
///     lists. The environment manager (E3.2 PR-B) goes through here; the active-selection + resolution
///     concern lives separately in <see cref="IEnvironmentService" />.
/// </summary>
public interface IEnvironmentStore
{
    /// <summary>The environments in the live workspace, newest edits reflected.</summary>
    IReadOnlyList<WorkspaceEnvironment> Environments { get; }

    /// <summary>
    ///     Inserts a new environment or replaces the existing one with the same
    ///     <see cref="WorkspaceEnvironment.Id" />, preserving the workspace's other state.
    /// </summary>
    Task SaveAsync(WorkspaceEnvironment environment, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Removes the environment and purges every secret-typed variable's stored value (SEC, FR-132).
    ///     A no-op if the id is unknown.
    /// </summary>
    Task DeleteAsync(string environmentId, CancellationToken cancellationToken = default);
}
