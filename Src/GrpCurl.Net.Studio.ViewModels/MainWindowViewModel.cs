using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GrpCurl.Net.Studio.ViewModels.Services;

namespace GrpCurl.Net.Studio.ViewModels;

/// <summary>
///     Root view model for the application shell. Owns the collapsible-pane state and the
///     pane-toggle commands. Theming and the welcome/empty-state wiring are layered on in the
///     shell PR (E0.2 PR-B); this skeleton establishes the DI graph and pane mechanics.
/// </summary>
public sealed partial class MainWindowViewModel : ViewModelBase
{
    [ObservableProperty]
    private bool _isSidebarOpen = true;

    [ObservableProperty]
    private bool _isInspectorOpen = true;

    [ObservableProperty]
    private bool _isConsoleOpen = true;

    [ObservableProperty]
    private bool _isFocusMode;

    private (bool Sidebar, bool Inspector, bool Console)? _preFocusState;

    public MainWindowViewModel(IUiDispatcher uiDispatcher, ISettingsStore settingsStore)
    {
        // Validated to prove the DI graph resolves; retained as fields once the shell PR
        // (theming/persistence) actually consumes them.
        ArgumentNullException.ThrowIfNull(uiDispatcher);
        ArgumentNullException.ThrowIfNull(settingsStore);
    }

    public string Title => "GrpCurl.Net Studio";

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
}
