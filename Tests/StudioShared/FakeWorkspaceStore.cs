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

    public bool IsCurrentReadOnly { get; private set; }

    public event EventHandler? ReadOnlyChanged;

    /// <summary>Test helper: drive the read-only flag (and raise <see cref="ReadOnlyChanged" />) directly (FR-148).</summary>
    public void SetReadOnly(bool value)
    {
        if (IsCurrentReadOnly == value)
        {
            return;
        }

        IsCurrentReadOnly = value;
        ReadOnlyChanged?.Invoke(this, EventArgs.Empty);
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

    /// <summary>The workspace + path passed to the most recent <see cref="ExportAsync" />.</summary>
    public (WorkspaceModel Workspace, string Path)? LastExport { get; private set; }

    public Task ExportAsync(WorkspaceModel workspace, string path, CancellationToken cancellationToken = default)
    {
        // Export must not change the active workspace, path, or dirty state.
        LastExport = (workspace, path);
        return Task.CompletedTask;
    }

    /// <summary>Scripted result for <see cref="ReadAsync" /> (the workspace to merge); throws <see cref="ReadError" /> when set.</summary>
    public WorkspaceModel? ReadResult { get; set; }

    public Exception? ReadError { get; set; }

    public Task<WorkspaceModel> ReadAsync(string path, CancellationToken cancellationToken = default)
    {
        if (ReadError is not null)
        {
            throw ReadError;
        }

        return Task.FromResult(ReadResult ?? WorkspaceModel.Empty());
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
