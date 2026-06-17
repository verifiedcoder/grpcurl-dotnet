using System.Collections.Specialized;
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
        ITlsProfileStore? profileStore = null)
    {
        _theme = theme;
        _profileStore = profileStore;
        Connections = connections;
        Explorer = explorer;
        Console = console;
        Inspector = inspector;
        Documents = documents;

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

    public string Title => "GrpCurl.Net Studio";

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
}
