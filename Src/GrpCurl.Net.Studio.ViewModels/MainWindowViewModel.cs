using System.Collections.Specialized;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GrpCurl.Net.Studio.ViewModels.Connections;
using GrpCurl.Net.Studio.ViewModels.Documents;
using GrpCurl.Net.Studio.ViewModels.Models;
using GrpCurl.Net.Studio.ViewModels.Models.Connections;
using GrpCurl.Net.Studio.ViewModels.Models.History;
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
    private readonly IHistoryStore? _history;
    private readonly ISecretStore? _secrets;
    private readonly IUpdateService? _updates;

    private SavedConnection? _insecureConnection;

    /// <summary>SEC-014: full-width, non-dismissable banner while an open tab uses a skip-verify profile.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ReviewInsecureConnectionCommand))]
    private bool _isInsecureBannerVisible;

    [ObservableProperty]
    private string _insecureBannerText = string.Empty;

    /// <summary>FR-148: shown while the active workspace file is read-only on disk (autosave disabled).</summary>
    [ObservableProperty]
    private bool _isReadOnlyBannerVisible;

    /// <summary>SPEC-040 §8: shown while another live Studio instance holds the workspace lock.</summary>
    [ObservableProperty]
    private bool _isLockedBannerVisible;

    [ObservableProperty]
    private string _lockedBannerText = string.Empty;

    /// <summary>FR-156: set when a launch-time update check found a newer release; shown in the status bar.</summary>
    [ObservableProperty]
    private bool _isUpdateAvailable;

    [ObservableProperty]
    private string _updateAvailableText = string.Empty;

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
        IDialogService? dialogs = null,
        EnvironmentSwitcherViewModel? environment = null,
        IHistoryStore? history = null,
        ISecretStore? secrets = null,
        IUpdateService? updates = null)
    {
        _theme = theme;
        _profileStore = profileStore;
        _workspaceStore = workspaceStore;
        _history = history;
        _secrets = secrets;
        _updates = updates;
        _filePicker = filePicker;
        _dialogs = dialogs;
        Connections = connections;
        Explorer = explorer;
        Console = console;
        Inspector = inspector;
        Documents = documents;
        Session = session;
        Environment = environment;

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

        // FR-148 / SPEC-040 §8: the read-only and locked banners follow the active workspace file's state.
        if (_workspaceStore is not null)
        {
            _workspaceStore.ReadOnlyChanged += (_, _) => RefreshReadOnlyBanner();
            _workspaceStore.LockChanged += (_, _) => RefreshLockBanner();
        }

        RefreshReadOnlyBanner();
        RefreshLockBanner();
        RefreshRecents();
    }

    /// <summary>FR-148: the message shown in the read-only banner.</summary>
    public string ReadOnlyBannerText
        => "This workspace file is read-only. Changes won't be saved automatically — use File → Save As to keep them.";

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

    /// <summary>FR-148: shows the banner while the active workspace file is read-only on disk.</summary>
    public void RefreshReadOnlyBanner() => IsReadOnlyBannerVisible = _workspaceStore?.IsCurrentReadOnly ?? false;

    /// <summary>SPEC-040 §8: shows the "Locked by PID … on host since …" banner while a live foreign lock holds the file.</summary>
    public void RefreshLockBanner()
    {
        if (_workspaceStore is { IsLockedByAnother: true, ForeignLock: { } info })
        {
            IsLockedBannerVisible = true;
            LockedBannerText = $"Locked by PID {info.Pid} on {info.Machine} since {info.AcquiredUtc.ToLocalTime():HH:mm}.";
        }
        else
        {
            IsLockedBannerVisible = false;
            LockedBannerText = string.Empty;
        }
    }

    /// <summary>
    ///     The running app version for the status bar (P3 fix), from the same runtime version service the
    ///     settings/update UI uses — so a release bump no longer leaves the shell showing a stale literal.
    ///     Empty when no update service is wired (e.g. headless tests).
    /// </summary>
    public string Version => _updates is { } updates ? $"v{updates.CurrentVersion}" : string.Empty;

    /// <summary>FR-156: surface that a launch-time check found a newer release (the shell shows a status-bar link).</summary>
    public void ShowUpdateAvailable(string latestVersion)
    {
        UpdateAvailableText = $"Update available: {latestVersion}";
        IsUpdateAvailable = true;
    }

    /// <summary>FR-156: open Settings → Updates so the user can review and download the update (consent-respecting).</summary>
    [RelayCommand]
    private void OpenUpdateSettings() => Documents.OpenSettings();

    /// <summary>SPEC-040 §8: steal the advisory lock so this instance can edit + save (the previous holder degrades).</summary>
    [RelayCommand(CanExecute = nameof(CanManageWorkspaces))]
    private async Task TakeOverWorkspaceLock()
    {
        if (_workspaceStore is null)
        {
            return;
        }

        await _workspaceStore.TakeOverLockAsync();
        RefreshLockBanner();
        Session?.Refresh();
        OnPropertyChanged(nameof(Title));
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

    /// <summary>The status-bar environment switcher (FR-133), or null in bare unit constructions.</summary>
    public EnvironmentSwitcherViewModel? Environment { get; }

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

    /// <summary>FR-003: the console clears its unread indicator when shown, and accrues it while collapsed.</summary>
    partial void OnIsConsoleOpenChanged(bool value) => Console.SetActive(value);

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

    // ── Command palette (Ctrl+K, SPEC-020) ───────────────────────────────────

    /// <summary>Opens the command palette and runs the chosen action once it closes.</summary>
    [RelayCommand]
    private async Task OpenCommandPalette()
    {
        if (_dialogs is null)
        {
            return;
        }

        var items = BuildPaletteItems();
        await AddHistoryItemsAsync(items);

        var chosen = await _dialogs.ShowDialogAsync(new CommandPaletteViewModel(items));

        if (chosen is not null)
        {
            await chosen.InvokeAsync();
        }
    }

    // Command palette v2 (history navigation): the most recent replayable calls, newest first. Replay reuses
    // the History tab's path — resolve the connection by name and re-open a draft from the redacted request.
    private async Task AddHistoryItemsAsync(List<PaletteItem> items)
    {
        if (_history is null || _workspaceStore is null)
        {
            return;
        }

        var entries = await _history.ReadAllAsync();

        foreach (var entry in entries.Where(e => !e.Request.BodyTruncated).Reverse().Take(20))
        {
            var captured = entry;
            items.Add(new PaletteItem(
                $"Replay: {captured.Method} ({captured.Connection.Name})", "History",
                () => ReplayHistoryAsync(captured)));
        }
    }

    private async Task ReplayHistoryAsync(HistoryEntry entry)
    {
        if (_workspaceStore is null)
        {
            return;
        }

        if (HistoryReplay.ResolveConnection(_workspaceStore.Current, entry) is not { } connection)
        {
            if (_dialogs is not null)
            {
                await _dialogs.ShowMessageAsync("Connection no longer exists",
                    $"The connection '{entry.Connection.Name}' is not in the current workspace, so this entry cannot be replayed.");
            }

            return;
        }

        Documents.OpenInvocation(connection, entry.Method, HistoryReplay.ToPrefill(entry.Request));
    }

    /// <summary>Builds the palette entries: app commands, then connections, then their saved requests.</summary>
    private List<PaletteItem> BuildPaletteItems()
    {
        var items = new List<PaletteItem>
        {
            Command("New workspace", NewWorkspaceCommand),
            Command("New workspace with example connection", NewWorkspaceFromTemplateCommand),
            Command("Open workspace…", OpenWorkspaceCommand),
            Command("Save workspace", SaveWorkspaceCommand),
            Command("Save workspace as…", SaveWorkspaceAsCommand),
            Command("Reload workspace from disk", ReloadWorkspaceCommand),
            Command("Import workspace…", ImportWorkspaceCommand),
            Command("Export workspace…", ExportWorkspaceCommand),
            Sync("Add connection", () => Connections.AddConnectionCommand.Execute(null)),
            Sync("Open Settings", OpenSettings),
            Sync("Open History", OpenHistory),
            Sync("Toggle sidebar", ToggleSidebar),
            Sync("Toggle console", ToggleConsole),
            Sync("Toggle inspector", ToggleInspector),
            Sync("Toggle focus mode", ToggleFocusMode),
            ThemeItem("Theme: System", AppTheme.System),
            ThemeItem("Theme: Light", AppTheme.Light),
            ThemeItem("Theme: Dark", AppTheme.Dark)
        };

        foreach (var connection in Connections.Connections)
        {
            var item = connection;
            items.Add(new PaletteItem($"Go to connection: {item.Name}", "Connection",
                () => { SelectConnection(item); return Task.CompletedTask; }));

            foreach (var request in item.SavedRequests)
            {
                var saved = request;
                items.Add(new PaletteItem($"Open request: {saved.Name}", "Saved request", () => saved.OpenCommand.ExecuteAsync(null)));
            }
        }

        AddMethodItems(items);

        return items;
    }

    // Command palette v2 (method navigation): jump straight to any method of the active connection's loaded
    // services. The explorer already holds the catalog for the selected connection, so this is a synchronous
    // read; choosing a method opens a fresh invocation tab on it.
    private void AddMethodItems(List<PaletteItem> items)
    {
        if (Connections.SelectedConnection?.Connection is not { } connection)
        {
            return;
        }

        foreach (var service in Explorer.Services)
        {
            foreach (var method in service.Methods)
            {
                var symbol = method.FullName;
                items.Add(new PaletteItem($"Invoke method: {symbol}", "Method",
                    () =>
                    {
                        Documents.OpenInvocation(connection, symbol);
                        return Task.CompletedTask;
                    }));
            }
        }
    }

    private void SelectConnection(ConnectionListItemViewModel item) => Connections.SelectedConnection = item;

    private static PaletteItem Command(string title, IAsyncRelayCommand command)
        => new(title, "Command", () => command.ExecuteAsync(null));

    private static PaletteItem Sync(string title, Action action)
        => new(title, "Command", () => { action(); return Task.CompletedTask; });

    private PaletteItem ThemeItem(string title, AppTheme theme)
        => new(title, "Command", () => _theme.SetAsync(theme));

    // ── E3.1: workspace file operations (File menu) ──────────────────────────

    /// <summary>File → New: start a fresh empty workspace (untitled until Save As), after a dirty guard.</summary>
    [RelayCommand(CanExecute = nameof(CanManageWorkspaces))]
    private Task NewWorkspace() => CreateNewWorkspace(withStarterConnection: false);

    /// <summary>File → New with Example Connection: a fresh workspace seeded with the FR-149 starter connection.</summary>
    [RelayCommand(CanExecute = nameof(CanManageWorkspaces))]
    private Task NewWorkspaceFromTemplate() => CreateNewWorkspace(withStarterConnection: true);

    private async Task CreateNewWorkspace(bool withStarterConnection)
    {
        if (_workspaceStore is null || !await ConfirmDiscardIfDirtyAsync())
        {
            return;
        }

        _workspaceStore.NewWorkspace(withStarterConnection);
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

        // FR-148: an untitled or read-only file has nowhere to autosave/flush — route to Save As instead
        // (which runs the secret guard itself).
        if (_workspaceStore.CurrentPath is null || _workspaceStore.IsCurrentReadOnly)
        {
            await SaveWorkspaceAs();
            return;
        }

        if (!await PassesSecretGuardAsync())
        {
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
            if (!await PassesSecretGuardAsync())
            {
                return;
            }

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

    /// <summary>File → Export Workspace…: write a secret-free copy of the workspace to a chosen path (FR-164).</summary>
    [RelayCommand(CanExecute = nameof(CanManageWorkspaces))]
    private async Task ExportWorkspace()
    {
        if (_workspaceStore is null || _filePicker is null)
        {
            return;
        }

        var suggested = $"{Sanitize(_workspaceStore.Current.Name)}.gcnws.json";
        var path = await _filePicker.SaveFileAsync("Export workspace", suggested, ["gcnws.json"]);

        if (path is not null)
        {
            if (!await PassesSecretGuardAsync())
            {
                return;
            }

            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            await _workspaceStore.ExportAsync(_workspaceStore.Current, path);
            var ms = stopwatch.Elapsed.TotalMilliseconds;

            // FR-004: mirror the export to the console (workspace name + duration; the file holds no secret literals).
            Console.AppendCall(new ConsoleCallActivity(
                $"Export workspace: {_workspaceStore.Current.Name}", 0, "written", false, $"{ms:0} ms",
                [new CallTimingPhase("export", $"{ms:0} ms", 1.0)],
                ConsoleActivityKind.Export, DateTimeOffset.UtcNow));
        }
    }

    // SEC-034 (T1 backstop): before an explicit save/export, scan the workspace for secret material that would
    // land as a plain literal in the committed file. Findings are surfaced honestly and the user confirms before
    // proceeding (autosave can't prompt, so the guard runs only on explicit writes). Returns true to proceed.
    private async Task<bool> PassesSecretGuardAsync()
    {
        if (_workspaceStore is null)
        {
            return true;
        }

        var workspace = _workspaceStore.Current;
        var leaks = WorkspaceSecretScanner.Scan(workspace, await ResolveSecretValuesAsync(workspace));

        if (leaks.Count == 0 || _dialogs is null)
        {
            return true;
        }

        var detail = string.Join("\n", leaks.Take(10).Select(Describe));

        return await _dialogs.ConfirmAsync(
            "Possible secret in workspace",
            "This workspace may hold secret values as plain text, which would be written to the file:\n\n"
            + detail
            + "\n\nUse a ${VAR} reference or a secret-typed variable instead to keep them out of the file. Save anyway?");

        static string Describe(SecretLeak leak) => leak.Kind == SecretLeakKind.SensitiveHeaderLiteral
            ? $"• {leak.Location} has a literal value (use a ${{VAR}} reference)"
            : $"• {leak.Location} matches a stored secret value";
    }

    private async Task<IReadOnlyCollection<string>> ResolveSecretValuesAsync(WorkspaceModel workspace)
    {
        if (_secrets is null)
        {
            return [];
        }

        var values = new List<string>();

        foreach (var environment in workspace.Environments)
        {
            foreach (var variable in environment.Variables)
            {
                if (variable.IsSecret && variable.Value.SecretRef is { } keyRef
                    && await _secrets.GetAsync(keyRef) is { } value)
                {
                    values.Add(value);
                }
            }
        }

        return values;
    }

    /// <summary>
    ///     File → Import Workspace…: merge another workspace into this one (FR-164). Connections, profiles,
    ///     and environments are added (never overwritten); a pre-merge summary lists what will change and the
    ///     user confirms before anything is applied.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanManageWorkspaces))]
    private async Task ImportWorkspace()
    {
        if (_workspaceStore is null || _filePicker is null || _dialogs is null)
        {
            return;
        }

        var path = await _filePicker.OpenFileAsync("Import workspace", ["gcnws.json", "json"]);

        if (path is null)
        {
            return;
        }

        WorkspaceModel incoming;

        try
        {
            incoming = await _workspaceStore.ReadAsync(path);
        }
        catch (WorkspaceSchemaException ex)
        {
            await _dialogs.ShowMessageAsync("Could not import workspace", ex.Message);
            return;
        }

        var (merged, summary) = WorkspaceMerger.Merge(_workspaceStore.Current, incoming);

        if (summary.IsEmpty)
        {
            await _dialogs.ShowMessageAsync("Nothing to import", summary.Describe());
            return;
        }

        if (!await _dialogs.ConfirmAsync(
                $"Import {summary.TotalAdded} item(s) from '{Path.GetFileName(path)}'?", summary.Describe()))
        {
            return;
        }

        // SEC-041: offer to supply the dangling secrets inline (masked). The imported keyrefs are preserved by
        // the merge, so storing a value under the same keyref makes the imported item resolve locally.
        if (summary.MissingSecrets.Count > 0 && _secrets is not null
            && await _dialogs.ShowDialogAsync(new ImportSecretsDialogViewModel(summary.MissingSecrets)) is { } supplied)
        {
            foreach (var (keyRef, value) in supplied)
            {
                await _secrets.SetAsync(keyRef, value);
            }
        }

        await _workspaceStore.SaveAsync(merged);
        OnWorkspaceSwitched();
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
        Environment?.Reload(); // the new workspace carries its own environments (FR-133/138)
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
