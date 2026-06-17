using GrpCurl.Net.Studio.ViewModels.Models.Connections;
using GrpCurl.Net.Studio.ViewModels.Services;

namespace GrpCurl.Net.Studio.TestSupport;

/// <summary>In-memory <see cref="IWorkspaceStore" /> recording saves, opens, and recents.</summary>
public sealed class FakeWorkspaceStore : IWorkspaceStore
{
    private readonly List<string> _recent = [];

    public FakeWorkspaceStore(WorkspaceModel? initial = null) => Current = initial ?? WorkspaceModel.Empty();

    public WorkspaceModel Current { get; private set; }

    public string? CurrentPath { get; private set; }

    public bool IsDirty { get; private set; }

    public event EventHandler? DirtyChanged;

    public int SaveCount { get; private set; }

    public int SaveNowCount { get; private set; }

    public int ReloadCount { get; private set; }

    public string? LastSavedAsPath { get; private set; }

    /// <summary>Scripted result for <see cref="OpenAsync" />; throws <see cref="OpenError" /> when it is set.</summary>
    public WorkspaceModel? OpenResult { get; set; }

    public Exception? OpenError { get; set; }

    /// <summary>Scripted result + error for <see cref="ReloadAsync" />.</summary>
    public WorkspaceModel? ReloadResult { get; set; }

    public Exception? ReloadError { get; set; }

    /// <summary>Test helper: drive the dirty flag (and raise <see cref="DirtyChanged" />) directly.</summary>
    public void SetDirty(bool value)
    {
        if (IsDirty == value)
        {
            return;
        }

        IsDirty = value;
        DirtyChanged?.Invoke(this, EventArgs.Empty);
    }

    public IReadOnlyList<RecentWorkspace> RecentWorkspaces
        => _recent.Select(p => new RecentWorkspace(p, Exists: true)).ToList();

    public void SeedRecent(params string[] paths)
    {
        _recent.Clear();
        _recent.AddRange(paths);
    }

    public Task<WorkspaceModel> LoadAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(Current);

    public Task<WorkspaceModel> OpenAsync(string path, CancellationToken cancellationToken = default)
    {
        if (OpenError is not null)
        {
            throw OpenError;
        }

        Current = OpenResult ?? Current;
        CurrentPath = path;
        _recent.Remove(path);
        _recent.Insert(0, path);
        return Task.FromResult(Current);
    }

    public Task SaveAsync(WorkspaceModel workspace, CancellationToken cancellationToken = default)
    {
        Current = workspace;
        SaveCount++;
        SetDirty(false);
        return Task.CompletedTask;
    }

    public Task SaveNowAsync(CancellationToken cancellationToken = default)
    {
        SaveNowCount++;
        SetDirty(false);
        return Task.CompletedTask;
    }

    public Task ReloadAsync(CancellationToken cancellationToken = default)
    {
        ReloadCount++;

        if (ReloadError is not null)
        {
            throw ReloadError;
        }

        Current = ReloadResult ?? Current;
        SetDirty(false);
        return Task.CompletedTask;
    }

    public Task SaveAsAsync(WorkspaceModel workspace, string path, CancellationToken cancellationToken = default)
    {
        Current = workspace;
        CurrentPath = path;
        LastSavedAsPath = path;
        _recent.Remove(path);
        _recent.Insert(0, path);
        SaveCount++;
        return Task.CompletedTask;
    }

    public WorkspaceModel NewWorkspace()
    {
        Current = WorkspaceModel.Empty();
        CurrentPath = null;
        return Current;
    }

    public Task RemoveRecentAsync(string path, CancellationToken cancellationToken = default)
    {
        _recent.Remove(path);
        return Task.CompletedTask;
    }
}
