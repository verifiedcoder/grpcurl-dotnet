using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.VisualTree;
using GrpCurl.Net.Studio.ViewModels;

namespace GrpCurl.Net.Studio.Views;

public sealed partial class MainWindow : Window
{
    // The F6 focus-zone order (SPEC-020 §6): sidebar → document → inspector → console → status bar.
    private static readonly string[] ZoneNames = ["SidebarZone", "CentreZone", "InspectorZone", "ConsoleZone", "StatusBar"];

    // Ctrl+L toggles: first press focuses the explorer filter; a second press returns focus where it was.
    private IInputElement? _focusBeforeFilter;

    public MainWindow() => InitializeComponent();

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    private void OnExitClicked(object? sender, RoutedEventArgs e) => Close();

    // SPEC-020 §5–6 focus moves. These target controls rather than commands, so they live here rather
    // than in Window.KeyBindings. Handled on tunnel so they win even while an editor holds focus.
    protected override void OnKeyDown(KeyEventArgs e)
    {
        var ctrl = e.KeyModifiers.HasFlag(KeyModifiers.Control);

        switch (e.Key)
        {
            case Key.L when ctrl:
                FocusExplorerFilter();
                e.Handled = true;
                break;
            case Key.E when ctrl:
                FocusEnvironmentSwitcher();
                e.Handled = true;
                break;
            case Key.F6:
                CycleZone(forward: !e.KeyModifiers.HasFlag(KeyModifiers.Shift));
                e.Handled = true;
                break;
            default:
                base.OnKeyDown(e);
                break;
        }
    }

    // Ctrl+L (SPEC-020 §5): focus the explorer filter, expanding the sidebar first if it is collapsed.
    // A second press while the filter is focused restores focus to where it was.
    private void FocusExplorerFilter()
    {
        var filter = this.GetVisualDescendants().OfType<TextBox>().FirstOrDefault(t => t.Name == "FilterBox");

        if (filter is null)
        {
            return;
        }

        if (filter.IsFocused && _focusBeforeFilter is not null)
        {
            _ = _focusBeforeFilter.Focus();
            _focusBeforeFilter = null;
            return;
        }

        if (DataContext is MainWindowViewModel vm)
        {
            vm.IsSidebarOpen = true;
        }

        _focusBeforeFilter = FocusManager?.GetFocusedElement();
        _ = filter.Focus();
        filter.SelectAll();
    }

    // Ctrl+E (SPEC-020 §5): focus the status-bar environment switcher and drop it open for arrow+Enter selection.
    private void FocusEnvironmentSwitcher()
    {
        if (this.FindControl<ComboBox>("EnvironmentSwitcher") is { IsEffectivelyVisible: true, IsEnabled: true } combo)
        {
            _ = combo.Focus();
            combo.IsDropDownOpen = true;
        }
    }

    // F6 (SPEC-020 §6): move focus to the first focusable control in the next visible zone, wrapping.
    private void CycleZone(bool forward)
    {
        var zones = ZoneNames
            .Select(name => this.FindControl<Control>(name))
            .Where(zone => zone is { IsEffectivelyVisible: true })
            .Cast<Control>()
            .ToList();

        if (zones.Count == 0)
        {
            return;
        }

        var focused = FocusManager?.GetFocusedElement() as Visual;
        var currentZone = focused is null ? -1 : zones.FindIndex(zone => focused.GetSelfAndVisualAncestors().Contains(zone));

        for (var step = 1; step <= zones.Count; step++)
        {
            var index = currentZone < 0
                ? (forward ? step - 1 : zones.Count - step)
                : (((currentZone + (forward ? step : -step)) % zones.Count) + zones.Count) % zones.Count;

            if (FocusFirstFocusable(zones[index]))
            {
                return;
            }
        }
    }

    private static bool FocusFirstFocusable(Visual zone)
    {
        foreach (var control in zone.GetSelfAndVisualDescendants().OfType<Control>())
        {
            if (control is { Focusable: true, IsEffectivelyVisible: true, IsEffectivelyEnabled: true })
            {
                return control.Focus();
            }
        }

        return false;
    }
}
