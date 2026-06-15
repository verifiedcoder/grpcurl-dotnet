using GrpCurl.Net.Studio.ViewModels.Models.Connections;

namespace GrpCurl.Net.Studio.ViewModels.Services;

/// <summary>
///     Loads and persists the workspace (connection list, SPEC-040). Phase 1 manages a single
///     default workspace in the config directory; E3.1 generalizes to multiple named workspaces.
/// </summary>
public interface IWorkspaceStore
{
    WorkspaceModel Current { get; }

    Task<WorkspaceModel> LoadAsync(CancellationToken cancellationToken = default);

    Task SaveAsync(WorkspaceModel workspace, CancellationToken cancellationToken = default);
}
