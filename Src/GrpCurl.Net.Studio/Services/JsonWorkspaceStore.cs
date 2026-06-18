using System.Text.Json;
using GrpCurl.Net.Studio.ViewModels.Models.Connections;
using GrpCurl.Net.Studio.ViewModels.Services;

namespace GrpCurl.Net.Studio.Services;

/// <summary>
///     Persists workspaces as <c>.gcnws.json</c> documents (SPEC-040 §3) and tracks the recently used
///     paths in <c>recent-workspaces.json</c> (§1). The default startup workspace lives in the per-OS
///     config directory and loads resiliently; <see cref="OpenAsync" /> reads documents from anywhere
///     strictly (surfacing <see cref="WorkspaceSchemaException" />). All writes are atomic temp-file moves
///     through <see cref="WorkspaceSerializer" /> (canonical LF / no-BOM format).
/// </summary>
internal sealed class JsonWorkspaceStore : IWorkspaceStore
{
    private const string AppFolderName = "GrpCurlNet.Studio";
    private const string DefaultFileName = "workspace.json";
    private const string RecentFileName = "recent-workspaces.json";
    private const int MaxRecent = 10;

    private readonly string _defaultPath;
    private readonly string _recentPath;
    private readonly TimeSpan _debounce;
    private readonly List<string> _recent = [];

    private CancellationTokenSource? _flushCts;
    private bool _isDirty;
    private bool _isReadOnly;

    public JsonWorkspaceStore()
        : this(
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                AppFolderName,
                DefaultFileName),
            autosaveDebounce: TimeSpan.FromSeconds(1))
    {
    }

    // Test seam: point the store at a temp file; recents live beside it. Tests default to a zero
    // debounce so an autosave flushes synchronously within the awaited SaveAsync.
    internal JsonWorkspaceStore(string path, TimeSpan? autosaveDebounce = null)
    {
        _defaultPath = path;
        CurrentPath = path;
        _debounce = autosaveDebounce ?? TimeSpan.Zero;
        _recentPath = Path.Combine(Path.GetDirectoryName(path) ?? ".", RecentFileName);
        LoadRecent();
    }

    public WorkspaceModel Current { get; private set; } = WorkspaceModel.Empty();

    public string? CurrentPath { get; private set; }

    public bool IsDirty => _isDirty;

    public event EventHandler? DirtyChanged;

    public bool IsCurrentReadOnly => _isReadOnly;

    public event EventHandler? ReadOnlyChanged;

    public IReadOnlyList<RecentWorkspace> RecentWorkspaces
        => _recent.Select(p => new RecentWorkspace(p, File.Exists(p))).ToList();

    public async Task<WorkspaceModel> LoadAsync(CancellationToken cancellationToken = default)
    {
        CurrentPath = _defaultPath;
        RefreshReadOnly(_defaultPath);

        if (!File.Exists(_defaultPath))
        {
            return Current = WorkspaceModel.Empty();
        }

        try
        {
            var json = await File.ReadAllTextAsync(_defaultPath, cancellationToken).ConfigureAwait(false);
            Current = DeserializeResolved(json, _defaultPath);
        }
        catch (Exception ex) when (ex is WorkspaceSchemaException or IOException)
        {
            // The default startup workspace stays resilient: a corrupt/newer file is set aside so the app
            // always starts. OpenAsync is the strict, user-facing path that surfaces the error instead.
            TryQuarantine(_defaultPath);
            Current = WorkspaceModel.Empty();
        }

        SetDirty(false);
        return Current;
    }

    public async Task<WorkspaceModel> OpenAsync(string path, CancellationToken cancellationToken = default)
    {
        var json = await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);

        // Strict: WorkspaceSerializer throws WorkspaceSchemaException for a corrupt/newer file. We let it
        // propagate so the open flow can show the message, leaving Current/CurrentPath untouched.
        var workspace = DeserializeResolved(json, path);

        CancelPendingFlush();
        Current = workspace;
        CurrentPath = path;
        SetDirty(false);
        RefreshReadOnly(path);
        await PromoteRecentAsync(path, cancellationToken).ConfigureAwait(false);
        return workspace;
    }

    public async Task SaveAsync(WorkspaceModel workspace, CancellationToken cancellationToken = default)
    {
        // Apply the mutation in memory and autosave: flush now when the debounce is zero, otherwise
        // schedule a debounced flush. An untitled workspace (no path) stays dirty until Save As.
        Current = workspace;
        SetDirty(true);
        CancelPendingFlush();

        if (_isReadOnly)
        {
            return; // FR-148: autosave is disabled for a read-only file; the change stays in memory (dirty).
        }

        if (_debounce <= TimeSpan.Zero)
        {
            await FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        else
        {
            ScheduleFlush();
        }
    }

    public async Task SaveNowAsync(CancellationToken cancellationToken = default)
    {
        CancelPendingFlush();
        await FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task ReloadAsync(CancellationToken cancellationToken = default)
    {
        if (CurrentPath is null)
        {
            return; // untitled — nothing on disk to reload from
        }

        CancelPendingFlush();
        var json = await File.ReadAllTextAsync(CurrentPath, cancellationToken).ConfigureAwait(false);
        Current = DeserializeResolved(json, CurrentPath); // strict — surfaces a corrupt/newer file
        SetDirty(false);
        RefreshReadOnly(CurrentPath); // the file's permissions may have changed since open
    }

    public async Task SaveAsAsync(WorkspaceModel workspace, string path, CancellationToken cancellationToken = default)
    {
        CancelPendingFlush();
        await WriteAtomicAsync(path, workspace, cancellationToken).ConfigureAwait(false);
        Current = workspace;
        CurrentPath = path;
        SetDirty(false);
        RefreshReadOnly(path); // FR-148: Save As to a writable path clears read-only
        await PromoteRecentAsync(path, cancellationToken).ConfigureAwait(false);
    }

    public Task ExportAsync(WorkspaceModel workspace, string path, CancellationToken cancellationToken = default)
        // Export is a plain copy to disk: it must not disturb the active file, dirty state, or recents.
        => WriteAtomicAsync(path, workspace, cancellationToken);

    public async Task<WorkspaceModel> ReadAsync(string path, CancellationToken cancellationToken = default)
    {
        // Read-only preview for a merge: deserialize strictly, but leave Current/CurrentPath/recents alone.
        var json = await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
        return DeserializeResolved(json, path);
    }

    public WorkspaceModel NewWorkspace()
    {
        CancelPendingFlush();
        Current = WorkspaceModel.Empty();
        CurrentPath = null; // untitled until the first Save As
        SetDirty(false);
        SetReadOnly(false); // a fresh untitled workspace has nothing on disk
        return Current;
    }

    public async Task RemoveRecentAsync(string path, CancellationToken cancellationToken = default)
    {
        if (_recent.RemoveAll(p => PathsEqual(p, path)) > 0)
        {
            await PersistRecentAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>Writes the current workspace to its path and clears dirty; a no-op for an untitled workspace.</summary>
    private async Task FlushAsync(CancellationToken cancellationToken)
    {
        if (CurrentPath is null || _isReadOnly)
        {
            return; // untitled (no path) or read-only (FR-148) — no write; stays dirty until Save As
        }

        await WriteAtomicAsync(CurrentPath, Current, cancellationToken).ConfigureAwait(false);
        SetDirty(false);
    }

    private void ScheduleFlush()
    {
        var cts = new CancellationTokenSource();
        _flushCts = cts;
        _ = DelayedFlushAsync(cts.Token);
    }

    private async Task DelayedFlushAsync(CancellationToken token)
    {
        try
        {
            await Task.Delay(_debounce, token).ConfigureAwait(false);
            await FlushAsync(token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Superseded by a newer mutation or an explicit save/reload.
        }
    }

    private void CancelPendingFlush()
    {
        _flushCts?.Cancel();
        _flushCts = null;
    }

    private void SetDirty(bool value)
    {
        if (_isDirty == value)
        {
            return;
        }

        _isDirty = value;
        DirtyChanged?.Invoke(this, EventArgs.Empty);
    }

    // FR-148: a backing file is read-only when it exists on disk and the OS marks it non-writable
    // (Windows ReadOnly attribute, or no owner write permission on Unix). Untitled = never read-only.
    private void RefreshReadOnly(string? path)
        => SetReadOnly(path is not null && File.Exists(path) && new FileInfo(path).IsReadOnly);

    private void SetReadOnly(bool value)
    {
        if (_isReadOnly == value)
        {
            return;
        }

        _isReadOnly = value;
        ReadOnlyChanged?.Invoke(this, EventArgs.Empty);
    }

    // Deserialise then resolve FR-147 relative file references back to absolute against the file's directory,
    // so the in-memory model always holds absolute paths regardless of how they were stored on disk.
    private static WorkspaceModel DeserializeResolved(string json, string filePath)
        => WorkspacePathPortability.ToAbsolute(WorkspaceSerializer.Deserialize(json), DirectoryOf(filePath));

    private static string DirectoryOf(string path) => Path.GetDirectoryName(Path.GetFullPath(path))!;

    private static async Task WriteAtomicAsync(string path, WorkspaceModel workspace, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        // FR-147: store file references relative to this file's directory when they live beneath it.
        var portable = WorkspacePathPortability.ToRelative(workspace, DirectoryOf(path));

        var tempPath = path + ".tmp";
        await File.WriteAllBytesAsync(tempPath, WorkspaceSerializer.SerializeToUtf8(portable), cancellationToken).ConfigureAwait(false);

        File.Move(tempPath, path, overwrite: true);
    }

    private async Task PromoteRecentAsync(string path, CancellationToken cancellationToken)
    {
        var full = Path.GetFullPath(path);
        _recent.RemoveAll(p => PathsEqual(p, full));
        _recent.Insert(0, full);

        if (_recent.Count > MaxRecent)
        {
            _recent.RemoveRange(MaxRecent, _recent.Count - MaxRecent);
        }

        await PersistRecentAsync(cancellationToken).ConfigureAwait(false);
    }

    private void LoadRecent()
    {
        if (!File.Exists(_recentPath))
        {
            return;
        }

        try
        {
            var paths = JsonSerializer.Deserialize(File.ReadAllText(_recentPath), RecentWorkspacesJsonContext.Default.ListString);

            if (paths is not null)
            {
                _recent.AddRange(paths.Where(p => !string.IsNullOrWhiteSpace(p)).Take(MaxRecent));
            }
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            // Cosmetic data: a corrupt recents file is simply ignored (regenerated empty on next write).
        }
    }

    private async Task PersistRecentAsync(CancellationToken cancellationToken)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_recentPath)!);
            var json = JsonSerializer.Serialize(_recent, RecentWorkspacesJsonContext.Default.ListString);
            await File.WriteAllTextAsync(_recentPath, json, cancellationToken).ConfigureAwait(false);
        }
        catch (IOException)
        {
            // Best effort — the recents list is a convenience, never load-bearing.
        }
    }

    private static bool PathsEqual(string a, string b)
        => string.Equals(Path.GetFullPath(a), Path.GetFullPath(b),
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    private static void TryQuarantine(string path)
    {
        try
        {
            File.Move(path, path + ".corrupt", overwrite: true);
        }
        catch (IOException)
        {
            // Best effort.
        }
    }
}
