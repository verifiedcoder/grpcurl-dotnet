using CommunityToolkit.Mvvm.Input;
using GrpCurl.Net.Studio.ViewModels.Services;

namespace GrpCurl.Net.Studio.ViewModels;

/// <summary>
///     The shell's view over the active workspace (SPEC-040 §1, SPEC-020 status bar): its display name,
///     file label, and dirty state, plus explicit Save and Reload-from-disk commands. Mutations autosave
///     through the store (debounced); this surfaces the dirty dot between change and flush. Reload re-reads
///     the file, warning first when there are unsaved changes (no file-watching in v1, by design).
/// </summary>
public sealed partial class WorkspaceSessionViewModel : ViewModelBase
{
    private readonly IWorkspaceStore _store;
    private readonly IDialogService _dialogs;

    public WorkspaceSessionViewModel(IWorkspaceStore store, IDialogService dialogs)
    {
        _store = store;
        _dialogs = dialogs;
        _store.DirtyChanged += OnDirtyChanged;
        _store.ReadOnlyChanged += OnReadOnlyChanged;
    }

    /// <summary>The workspace's display name.</summary>
    public string WorkspaceName => _store.Current.Name;

    /// <summary>The file name backing the workspace, or "Untitled" before the first Save As.</summary>
    public string FileLabel => _store.CurrentPath is { } path ? Path.GetFileName(path) : "Untitled";

    public bool IsDirty => _store.IsDirty;

    /// <summary>FR-148: the active file is read-only on disk (autosave disabled; Save As to keep changes).</summary>
    public bool IsReadOnly => _store.IsCurrentReadOnly;

    /// <summary>
    ///     Status-bar text: the file label with a dirty dot, e.g. <c>project.gcnws.json ●</c>, or a
    ///     <c>— read-only</c> suffix when the file can't be written (FR-148).
    /// </summary>
    public string StatusText => IsReadOnly
        ? $"{FileLabel} — read-only"
        : IsDirty
            ? $"{FileLabel} ●"
            : FileLabel;

    private bool CanReload => _store.CurrentPath is not null;

    /// <summary>Flushes any pending autosave to disk immediately (explicit Save).</summary>
    [RelayCommand]
    private Task Save() => _store.SaveNowAsync();

    /// <summary>
    ///     Reloads the workspace from disk, discarding unsaved changes after a confirmation. A corrupt or
    ///     newer file on disk is reported and the in-memory workspace is left as-is. Returns
    ///     <see langword="true" /> when the on-disk state was actually loaded (so the shell can refresh
    ///     dependent panes), <see langword="false" /> when the user declined or the reload failed.
    /// </summary>
    public async Task<bool> ReloadAsync()
    {
        if (IsDirty && !await _dialogs.ConfirmAsync(
                "Reload from disk?",
                "This workspace has unsaved changes. Reloading will discard them and re-read the file on disk. Continue?"))
        {
            return false;
        }

        try
        {
            await _store.ReloadAsync();
        }
        catch (WorkspaceSchemaException ex)
        {
            await _dialogs.ShowMessageAsync("Could not reload workspace", ex.Message);
            return false;
        }

        Refresh();
        return true;
    }

    [RelayCommand(CanExecute = nameof(CanReload))]
    private Task Reload() => ReloadAsync();

    /// <summary>Re-publishes the workspace identity/state after it changes (open / new / save-as).</summary>
    public void Refresh()
    {
        OnPropertyChanged(nameof(WorkspaceName));
        OnPropertyChanged(nameof(FileLabel));
        OnPropertyChanged(nameof(IsDirty));
        OnPropertyChanged(nameof(IsReadOnly));
        OnPropertyChanged(nameof(StatusText));
        ReloadCommand.NotifyCanExecuteChanged();
    }

    private void OnDirtyChanged(object? sender, EventArgs e)
    {
        OnPropertyChanged(nameof(IsDirty));
        OnPropertyChanged(nameof(StatusText));
    }

    private void OnReadOnlyChanged(object? sender, EventArgs e)
    {
        OnPropertyChanged(nameof(IsReadOnly));
        OnPropertyChanged(nameof(StatusText));
    }
}
