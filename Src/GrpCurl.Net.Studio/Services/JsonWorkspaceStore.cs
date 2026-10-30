using GrpCurl.Net.Studio.ViewModels.Models.Connections;
using GrpCurl.Net.Studio.ViewModels.Services;
using System.Text.Json;

namespace GrpCurl.Net.Studio.Services;

/// <summary>
///     Persists workspaces as <c>.gcnws.json</c> documents (SPEC-040 §3) and tracks the recently used
///     paths in <c>recent-workspaces.json</c> (§1). The default startup workspace lives in the per-OS
///     config directory and loads resiliently; <see cref="OpenAsync" /> reads documents from anywhere
///     strictly (surfacing <see cref="WorkspaceSchemaException" />). All writes are atomic temp-file moves
///     through <see cref="WorkspaceSerializer" /> (canonical LF / no-BOM format).
/// </summary>
internal sealed partial class JsonWorkspaceStore : IWorkspaceStore
{
    private const string AppFolderName = "GrpCurlNet.Studio";
    private const string DefaultFileName = "workspace.json";
    private const string RecentFileName = "recent-workspaces.json";
    private const int MaxRecent = 10;

    private readonly string _defaultPath;
    private readonly string _recentPath;
    private readonly TimeSpan _debounce;
    private readonly WorkspaceLockManager _lock;
    private readonly List<string> _recent = [];

    private CancellationTokenSource? _flushCts;
    private bool _isDirty;
    private bool _isReadOnly;
    private bool _isLockedByAnother;
    private bool _haveLock;
    private WorkspaceLockInfo? _foreignLock;

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
    // debounce so an autosave flushes synchronously within the awaited SaveAsync, and inject a lock
    // manager with a deterministic identity/clock to exercise the SPEC-040 §8 multi-instance rules.
    internal JsonWorkspaceStore(string path, TimeSpan? autosaveDebounce = null, WorkspaceLockManager? lockManager = null, WorkspaceTrustStore? trust = null)
    {
        _defaultPath = path;
        CurrentPath = path;
        _debounce = autosaveDebounce ?? TimeSpan.Zero;
        _lock = lockManager ?? WorkspaceLockManager.Default();
        _recentPath = Path.Combine(Path.GetDirectoryName(path) ?? ".", RecentFileName);
        _trust = trust ?? new WorkspaceTrustStore(Path.Combine(Path.GetDirectoryName(path)!, "workspace-trust.json"));
        LoadRecent();
    }

    public WorkspaceModel Current { get; private set; } = WorkspaceModel.Empty();

    public string? CurrentPath { get; private set; }

    public bool IsDirty => _isDirty;

    public event EventHandler? DirtyChanged;

    public bool IsCurrentReadOnly => _isReadOnly;

    public event EventHandler? ReadOnlyChanged;

    public bool IsLockedByAnother => _isLockedByAnother;

    public WorkspaceLockInfo? ForeignLock => _foreignLock;

    public event EventHandler? LockChanged;

    public IReadOnlyList<RecentWorkspace> RecentWorkspaces
        => _recent.Select(p => new RecentWorkspace(p, File.Exists(p))).ToList();

    public async Task<WorkspaceModel> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_defaultPath))
        {
            CurrentPath = _defaultPath;
            RefreshReadOnly(_defaultPath);
            AcquireLockFor(_defaultPath);
            return Current = WorkspaceModel.Empty();
        }
        try
        {
            var prepared = await PrepareAsync(_defaultPath, cancellationToken).ConfigureAwait(false);
            if (prepared.RequiresApproval)
            {
                PendingStartupPath = _defaultPath;
                CurrentPath = null;
                return Current = WorkspaceModel.Empty();
            }
            return await CommitOpenAsync(prepared, cancellationToken).ConfigureAwait(false);
        }
        catch (WorkspaceTrustException ex)
        {
            // Trust failures must never quarantine or overwrite the selected document.
            TrustMessage = ex.Message;
            PendingStartupPath = _defaultPath;
            CurrentPath = null;
            return Current = WorkspaceModel.Empty();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            TrustMessage = "The startup workspace could not be read. Check its permissions and open it again. " + ex.Message;
            PendingStartupPath = _defaultPath;
            CurrentPath = null;
            return Current = WorkspaceModel.Empty();
        }
        catch (WorkspaceSchemaException)
        {
            TryQuarantine(_defaultPath);
            CurrentPath = _defaultPath;
            RefreshReadOnly(_defaultPath);
            AcquireLockFor(_defaultPath);
            SetDirty(false);
            return Current = WorkspaceModel.Empty();
        }
    }

    public async Task<WorkspaceModel> OpenAsync(string path, CancellationToken cancellationToken = default)
        => await CommitOpenAsync(await PrepareAsync(path, cancellationToken).ConfigureAwait(false), cancellationToken).ConfigureAwait(false);

    public async Task SaveAsync(WorkspaceModel workspace, CancellationToken cancellationToken = default)
    {
        // Apply the mutation in memory and autosave: flush now when the debounce is zero, otherwise
        // schedule a debounced flush. An untitled workspace (no path) stays dirty until Save As.
        await AuthorizeMutationAsync(workspace, AuthorizationPath, null, cancellationToken).ConfigureAwait(false);
        Current = WorkspacePathPortability.ToAbsolute(workspace, DirectoryOf(AuthorizationPath));
        SetDirty(true);
        CancelPendingFlush();

        if (_isReadOnly || _isLockedByAnother)
        {
            return; // FR-148 / SPEC-040 §8: autosave is off for a read-only or foreign-locked file (stays dirty).
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

        _ = await OpenAsync(CurrentPath, cancellationToken).ConfigureAwait(false);
    }

    public async Task SaveAsAsync(WorkspaceModel workspace, string path, CancellationToken cancellationToken = default)
    {
        path = Path.GetFullPath(path);
        var previousPersistence = _persistenceVersion;
        var previousApprovals = _activeApprovals;
        var previousTargets = _activeTargets;
        var previousGeneration = _generation;
        try
        {
            await AuthorizeMutationAsync(workspace, path, null, cancellationToken).ConfigureAwait(false);
            await PersistAuthorizedAsync(path, workspace, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            if (_persistenceVersion != previousPersistence)
            {
                CancelPendingFlush();
                ReleaseHeldLock();
                Current = WorkspacePathPortability.ToAbsolute(workspace, DirectoryOf(path));
                CurrentPath = path;
                SetDirty(false);
                RefreshReadOnly(path);
                AcquireLockFor(path);
            }
            else
            {
                _activeApprovals = previousApprovals;
                _activeTargets = previousTargets;
                _generation = previousGeneration;
            }
            throw;
        }
        CancelPendingFlush();
        ReleaseHeldLock();
        Current = WorkspacePathPortability.ToAbsolute(workspace, DirectoryOf(path));
        CurrentPath = path;
        SetDirty(false);
        RefreshReadOnly(path);
        AcquireLockFor(path);
        await PromoteRecentAsync(path, cancellationToken).ConfigureAwait(false);
    }

    public Task ExportAsync(WorkspaceModel workspace, string path, CancellationToken cancellationToken = default)
        // Export is a plain copy to disk: it must not disturb the active file, dirty state, or recents.
        => WriteAtomicAsync(path, workspace, cancellationToken);

    public async Task<WorkspaceModel> ReadAsync(string path, CancellationToken cancellationToken = default)
    {
        var prepared = await PrepareAsync(path, cancellationToken).ConfigureAwait(false);
        await ValidatePreparationAsync(prepared, cancellationToken).ConfigureAwait(false);
        return prepared.Workspace;
    }

    public WorkspaceModel NewWorkspace(bool withStarterConnection = false)
    {
        CancelPendingFlush();
        ReleaseHeldLock();
        Current = withStarterConnection ? WorkspaceModel.StarterTemplate() : WorkspaceModel.Empty();
        CurrentPath = null; // untitled until the first Save As
        PendingStartupPath = null;
        _admittedFingerprint = null;
        _admittedLocation = null;
        _admissionId = null;
        _activeApprovals = new(WorkspaceReferenceAssessment.Comparer);
        _activeTargets = new(WorkspaceReferenceAssessment.Comparer);
        _generation = _trust.Read().Generation;
        SetDirty(false);
        SetReadOnly(false); // a fresh untitled workspace has nothing on disk
        SetLocked(false, null);
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
        if (CurrentPath is null || _isReadOnly || _isLockedByAnother)
        {
            return; // untitled, read-only (FR-148), or foreign-locked (SPEC-040 §8) — no write; stays dirty
        }

        // SPEC-040 §8: a holder detects the loss of its lock on its next save attempt — if another instance
        // has taken over, degrade to read-only-locked instead of writing over them.
        if (_haveLock && !_lock.StillOwned(CurrentPath))
        {
            SetLocked(true, WorkspaceLockManager.Holder(CurrentPath));
            return;
        }

        await PersistAuthorizedAsync(CurrentPath, Current, cancellationToken).ConfigureAwait(false);
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
        catch (Exception ex) when (ex is WorkspaceTrustException or IOException or UnauthorizedAccessException)
        {
            TrustMessage = ex.Message;
            if (ApprovalDialogs is not null)
            {
                Task Show() => ApprovalDialogs.ShowMessageAsync("Workspace autosave needs attention", ex.Message, CancellationToken.None);
                if (Dispatcher is null) { await Show().ConfigureAwait(false); }
                else { await (await Dispatcher.InvokeAsync(Show).ConfigureAwait(false)).ConfigureAwait(false); }
            }
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

    // SPEC-040 §8: take the advisory lock for the new active file; a live foreign lock leaves us locked-out.
    private void AcquireLockFor(string path)
    {
        var result = _lock.Acquire(path);
        _haveLock = result.Acquired;
        SetLocked(!result.Acquired, result.Holder);
    }

    private void ReleaseHeldLock()
    {
        if (_haveLock && CurrentPath is not null)
        {
            _lock.Release(CurrentPath);
        }

        _haveLock = false;
    }

    private void SetLocked(bool locked, WorkspaceLockInfo? holder)
    {
        _foreignLock = locked ? holder : null;

        if (_isLockedByAnother == locked)
        {
            return;
        }

        _isLockedByAnother = locked;
        LockChanged?.Invoke(this, EventArgs.Empty);
    }

    public Task TakeOverLockAsync(CancellationToken cancellationToken = default)
    {
        if (CurrentPath is not null)
        {
            _lock.TakeOver(CurrentPath); // SPEC-040 §8: steal the lock; the previous holder degrades on its next save
            _haveLock = true;
            SetLocked(false, null);
        }

        return Task.CompletedTask;
    }

    public void ReleaseLock() => ReleaseHeldLock();

    private static string DirectoryOf(string path) => Path.GetDirectoryName(Path.GetFullPath(path))!;

    private static async Task<string> WriteAtomicAsync(string path, WorkspaceModel workspace, CancellationToken cancellationToken)
    {
        _ = Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        // FR-147: store file references relative to this file's directory when they live beneath it.
        var portable = WorkspacePathPortability.ToRelative(workspace, DirectoryOf(path));

        var bytes = WorkspaceSerializer.SerializeToUtf8(portable);
        var tempPath = path + ".tmp";
        await File.WriteAllBytesAsync(tempPath, bytes, cancellationToken).ConfigureAwait(false);
        File.Move(tempPath, path, overwrite: true);
        return Fingerprint(bytes);
    }

    private async Task PromoteRecentAsync(string path, CancellationToken cancellationToken)
    {
        var full = Path.GetFullPath(path);
        _ = _recent.RemoveAll(p => PathsEqual(p, full));
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
            _ = Directory.CreateDirectory(Path.GetDirectoryName(_recentPath)!);
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
