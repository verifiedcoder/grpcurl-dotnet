using GrpCurl.Net.Studio.ViewModels.Models.Connections;
using GrpCurl.Net.Studio.ViewModels.Services;

namespace GrpCurl.Net.Studio.TestSupport;

/// <summary>In-memory <see cref="IWorkspaceStore" /> recording saves.</summary>
public sealed class FakeWorkspaceStore : IWorkspaceStore
{
    public FakeWorkspaceStore(WorkspaceModel? initial = null) => Current = initial ?? WorkspaceModel.Empty();

    public WorkspaceModel Current { get; private set; }

    public int SaveCount { get; private set; }

    public Task<WorkspaceModel> LoadAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(Current);

    public Task SaveAsync(WorkspaceModel workspace, CancellationToken cancellationToken = default)
    {
        Current = workspace;
        SaveCount++;
        return Task.CompletedTask;
    }
}
