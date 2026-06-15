using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GrpCurl.Net.Studio.ViewModels.Documents;
using GrpCurl.Net.Studio.ViewModels.Models;
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
    private bool _hasAnyConnection;

    private (bool Sidebar, bool Inspector, bool Console)? _preFocusState;

    public MainWindowViewModel(
        IThemeService theme,
        ConnectionsPaneViewModel connections,
        ServiceExplorerViewModel explorer,
        ConsoleViewModel console,
        InspectorViewModel inspector,
        DocumentsViewModel documents)
    {
        _theme = theme;
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
    }

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
