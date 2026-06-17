using System.Collections.Specialized;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GrpCurl.Net.Studio.ViewModels.Documents;
using GrpCurl.Net.Studio.ViewModels.Models;
using GrpCurl.Net.Studio.ViewModels.Models.Connections;
using GrpCurl.Net.Studio.ViewModels.Panes;
using GrpCurl.Net.Studio.ViewModels.Services;

namespace GrpCurl.Net.Studio.ViewModels;

/// <summary>
///     Root view model for the application shell. Owns the collapsible-pane state, focus mode,
///     the active theme selection, and the welcome empty-state flag. Theme is owned by the shared
///     <see cref="IThemeService" /> (the same source the Settings screen drives); the app-layer
///     <c>ThemeManager</c> observes it and applies the corresponding Avalonia theme variant,
///     keeping this view model free of any UI-framework dependency.
/// </summary>
public sealed partial class MainWindowViewModel : ViewModelBase
{
    private readonly IThemeService _theme;
    private readonly ITlsProfileStore? _profileStore;
    private readonly IWorkspaceStore? _workspaceStore;
    private readonly IFilePickerService? _filePicker;
    private readonly IDialogService? _dialogs;

    private SavedConnection? _insecureConnection;

    /// <summary>SEC-014: full-width, non-dismissable banner while an open tab uses a skip-verify profile.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ReviewInsecureConnectionCommand))]
    private bool _isInsecureBannerVisible;

    [ObservableProperty]
    private string _insecureBannerText = string.Empty;

    [ObservableProperty]
    private bool _isSidebarOpen = true;

    [ObservableProperty]
    private bool _isInspectorOpen = true;

    [ObservableProperty]
    private bool _isConsoleOpen = true;

    [ObservableProperty]
    private bool _isFocusMode;

    [ObservableProperty]
    private AppTheme _selectedTheme = AppTheme.System;

    /// <summary>Drives the welcome empty-state: shown until the first connection exists (SPEC-020 §7).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowDocuments), nameof(ShowWelcome))]
    private bool _hasAnyConnection;

    private (bool Sidebar, bool Inspector, bool Console)? _preFocusState;

    public MainWindowViewModel(
        IThemeService theme,
        ConnectionsPaneViewModel connections,
        ServiceExplorerViewModel explorer,
        ConsoleViewModel console,
        InspectorViewModel inspector,
        DocumentsViewModel documents,
        ITlsProfileStore? profileStore = null,
        IWorkspaceStore? workspaceStore = null,
        WorkspaceSessionViewModel? session = null,
        IFilePickerService? filePicker = null,
        IDialogService? dialogs = null)
    {
        _theme = theme;
        _profileStore = profileStore;
        _workspaceStore = workspaceStore;
        _filePicker = filePicker;
        _dialogs = dialogs;
        Connections = connections;
        Explorer = explorer;
        Console = console;
        Inspector = inspector;
        Documents = documents;
        Session = session;

        _selectedTheme = theme.Current;
        theme.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(IThemeService.Current))
            {
                SelectedTheme = _theme.Current;
            }
        };

        // The welcome empty-state shows until the first connection exists (SPEC-020 §7).
        _hasAnyConnection = connections.HasConnections;
        connections.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ConnectionsPaneViewModel.HasConnections))
            {
                HasAnyConnection = connections.HasConnections;
            }
        };

        // SEC-014: the insecure banner appears/disappears as tabs open and close.
        documents.Documents.CollectionChanged += OnDocumentsChanged;
        RefreshInsecureBanner();

        // E3.1: the window title tracks the workspace name + dirty state.
        if (session is not null)
        {
            session.PropertyChanged += (_, _) => OnPropertyChanged(nameof(Title));
        }

        RefreshRecents();
    }

    /// <summary>
    ///     The document area is shown once there is anything to render in it — a connection exists (the
    ///     normal flow) <em>or</em> a tab is already open. Without the second condition, a document opened
    ///     before the first connection (e.g. File → Settings on a fresh workspace) lands behind the welcome
    ///     overlay and appears to do nothing.
    /// </summary>
    public bool ShowDocuments => HasAnyConnection || Documents.Documents.Count > 0;

    /// <summary>The welcome empty-state is shown only when the document area has nothing to show.</summary>
    public bool ShowWelcome => !ShowDocuments;

    private void OnDocumentsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        RefreshInsecureBanner();
        OnPropertyChanged(nameof(ShowDocuments));
        OnPropertyChanged(nameof(ShowWelcome));
    }

    /// <summary>
    ///     Recomputes the insecure-skip-verify banner: visible when any open tab targets a TLS connection
    ///     whose referenced profile has verification disabled (SEC-014 / SPEC-020 §3.4).
    /// </summary>
    public void RefreshInsecureBanner()
    {
        _insecureConnection = Documents.Documents
            .Select(d => d.TabConnection)
            .FirstOrDefault(IsInsecure);

        if (_insecureConnection is { } connection)
        {
            IsInsecureBannerVisible = true;
            InsecureBannerText =
                $"INSECURE: certificate verification disabled for connection \"{connection.Name}\". "
                + "Traffic is exposed to interception.";
        }
        else
        {
            IsInsecureBannerVisible = false;
            InsecureBannerText = string.Empty;
        }
    }

    private bool IsInsecure(SavedConnection? connection)
    {
        if (_profileStore is null || connection is not { Transport: TransportMode.Tls, TlsProfileId: { } id })
        {
            return false;
        }

        return _profileStore.Profiles.FirstOrDefault(p => p.Id == id) is { InsecureSkipVerify: true };
    }

    private bool CanReviewInsecureConnection => IsInsecureBannerVisible;

    /// <summary>"Review connection…" on the banner opens the offending connection's editor (SPEC-020 §3.4).</summary>
    [RelayCommand(CanExecute = nameof(CanReviewInsecureConnection))]
    private Task ReviewInsecureConnection()
        => _insecureConnection is { } connection ? Connections.ReviewConnectionAsync(connection) : Task.CompletedTask;

    /// <summary>Window title: the active workspace name with a dirty marker, plus the app name.</summary>
    public string Title => Session is { } session
        ? $"{session.WorkspaceName}{(session.IsDirty ? " ●" : string.Empty)} — GrpCurl.Net Studio"
        : "GrpCurl.Net Studio";

    /// <summary>The active workspace session (status + Save), or null in bare unit constructions.</summary>
    public WorkspaceSessionViewModel? Session { get; }

    /// <summary>Recently opened/saved workspaces for the File → Recent submenu (null danglers greyed).</summary>
    public System.Collections.ObjectModel.ObservableCollection<RecentWorkspace> RecentWorkspaces { get; } = [];

    public bool HasRecentWorkspaces => RecentWorkspaces.Count > 0;

    /// <summary>Whether the workspace file operations are wired (the store + picker are present).</summary>
    public bool CanManageWorkspaces => _workspaceStore is not null && _filePicker is not null;

    public ConnectionsPaneViewModel Connections { get; }

    public ServiceExplorerViewModel Explorer { get; }

    public ConsoleViewModel Console { get; }

    public InspectorViewModel Inspector { get; }

    public DocumentsViewModel Documents { get; }

    [RelayCommand]
    private void ToggleSidebar() => IsSidebarOpen = !IsSidebarOpen;

    [RelayCommand]
    private void ToggleInspector() => IsInspectorOpen = !IsInspectorOpen;

    [RelayCommand]
    private void ToggleConsole() => IsConsoleOpen = !IsConsoleOpen;

    /// <summary>
    ///     Focus mode collapses all panes to maximise the document area, remembering the prior
    ///     pane state so toggling back restores it.
    /// </summary>
    [RelayCommand]
    private void ToggleFocusMode()
    {
        if (!IsFocusMode)
        {
            _preFocusState = (IsSidebarOpen, IsInspectorOpen, IsConsoleOpen);
            IsSidebarOpen = false;
            IsInspectorOpen = false;
            IsConsoleOpen = false;
            IsFocusMode = true;
        }
        else
        {
            (IsSidebarOpen, IsInspectorOpen, IsConsoleOpen) = _preFocusState ?? (true, true, true);
            IsFocusMode = false;
        }
    }

    /// <summary>Selects a theme via the shared service (persists + applies the live variant).</summary>
    [RelayCommand]
    private Task SetTheme(AppTheme theme) => _theme.SetAsync(theme);

    /// <summary>Opens (or focuses) the Settings tab.</summary>
    [RelayCommand]
    private void OpenSettings() => Documents.OpenSettings();

    /// <summary>Opens (or focuses) the History tab (FR-120).</summary>
    [RelayCommand]
    private void OpenHistory() => Documents.OpenHistory();

    // ── E3.1: workspace file operations (File menu) ──────────────────────────

    /// <summary>File → New: start a fresh empty workspace (untitled until Save As), after a dirty guard.</summary>
    [RelayCommand(CanExecute = nameof(CanManageWorkspaces))]
    private async Task NewWorkspace()
    {
        if (_workspaceStore is null || !await ConfirmDiscardIfDirtyAsync())
        {
            return;
        }

        _workspaceStore.NewWorkspace();
        OnWorkspaceSwitched();
    }

    /// <summary>File → Open…: pick a <c>.gcnws.json</c> and load it strictly (errors are reported).</summary>
    [RelayCommand(CanExecute = nameof(CanManageWorkspaces))]
    private async Task OpenWorkspace()
    {
        if (_workspaceStore is null || _filePicker is null || !await ConfirmDiscardIfDirtyAsync())
        {
            return;
        }

        var path = await _filePicker.OpenFileAsync("Open workspace", ["gcnws.json", "json"]);

        if (path is not null)
        {
            await OpenPathAsync(path);
        }
    }

    /// <summary>File → Open Recent: load a remembered workspace (a dangling entry offers to be forgotten).</summary>
    [RelayCommand(CanExecute = nameof(CanManageWorkspaces))]
    private async Task OpenRecent(string? path)
    {
        if (_workspaceStore is null || string.IsNullOrWhiteSpace(path) || !await ConfirmDiscardIfDirtyAsync())
        {
            return;
        }

        if (!File.Exists(path))
        {
            if (_dialogs is not null
                && await _dialogs.ConfirmAsync("Workspace not found", $"'{path}' no longer exists. Remove it from the recent list?"))
            {
                await _workspaceStore.RemoveRecentAsync(path);
                RefreshRecents();
            }

            return;
        }

        await OpenPathAsync(path);
    }

    /// <summary>File → Forget recent: drop a recent entry without opening it.</summary>
    [RelayCommand(CanExecute = nameof(CanManageWorkspaces))]
    private async Task ForgetRecent(string? path)
    {
        if (_workspaceStore is not null && !string.IsNullOrWhiteSpace(path))
        {
            await _workspaceStore.RemoveRecentAsync(path);
            RefreshRecents();
        }
    }

    /// <summary>File → Save: flush the active workspace, or Save As when it is still untitled.</summary>
    [RelayCommand(CanExecute = nameof(CanManageWorkspaces))]
    private async Task SaveWorkspace()
    {
        if (_workspaceStore is null)
        {
            return;
        }

        if (_workspaceStore.CurrentPath is null)
        {
            await SaveWorkspaceAs();
            return;
        }

        await _workspaceStore.SaveNowAsync();
        Session?.Refresh();
        OnPropertyChanged(nameof(Title));
    }

    /// <summary>File → Save As…: pick a path, write the workspace there, and make it the active file.</summary>
    [RelayCommand(CanExecute = nameof(CanManageWorkspaces))]
    private async Task SaveWorkspaceAs()
    {
        if (_workspaceStore is null || _filePicker is null)
        {
            return;
        }

        var suggested = $"{Sanitize(_workspaceStore.Current.Name)}.gcnws.json";
        var path = await _filePicker.SaveFileAsync("Save workspace as", suggested, ["gcnws.json"]);

        if (path is not null)
        {
            await _workspaceStore.SaveAsAsync(_workspaceStore.Current, path);
            Session?.Refresh();
            RefreshRecents();
            OnPropertyChanged(nameof(Title));
        }
    }

    /// <summary>File → Reload from disk: re-read the active file (confirming first when dirty).</summary>
    [RelayCommand(CanExecute = nameof(CanManageWorkspaces))]
    private async Task ReloadWorkspace()
    {
        if (Session is not null && await Session.ReloadAsync())
        {
            OnWorkspaceSwitched();
        }
    }

    private async Task OpenPathAsync(string path)
    {
        try
        {
            await _workspaceStore!.OpenAsync(path);
            OnWorkspaceSwitched();
        }
        catch (WorkspaceSchemaException ex)
        {
            if (_dialogs is not null)
            {
                await _dialogs.ShowMessageAsync("Could not open workspace", ex.Message);
            }
        }
    }

    /// <summary>Refreshes the panes/tabs after the active workspace changes (open / new / reload).</summary>
    private void OnWorkspaceSwitched()
    {
        Documents.CloseAll();
        Connections.ReloadFromWorkspace();
        HasAnyConnection = Connections.HasConnections;
        Session?.Refresh();
        RefreshInsecureBanner();
        RefreshRecents();
        OnPropertyChanged(nameof(Title));
    }

    private void RefreshRecents()
    {
        RecentWorkspaces.Clear();

        foreach (var recent in _workspaceStore?.RecentWorkspaces ?? [])
        {
            RecentWorkspaces.Add(recent);
        }

        OnPropertyChanged(nameof(HasRecentWorkspaces));
    }

    private async Task<bool> ConfirmDiscardIfDirtyAsync()
    {
        if (Session is not { IsDirty: true } || _dialogs is null)
        {
            return true;
        }

        return await _dialogs.ConfirmAsync(
            "Discard unsaved changes?",
            "The current workspace has unsaved changes that will be lost. Continue?");
    }

    private static string Sanitize(string name)
    {
        var trimmed = string.IsNullOrWhiteSpace(name) ? "workspace" : name.Trim();
        return string.Join("_", trimmed.Split(Path.GetInvalidFileNameChars()));
    }
}
