using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Threading;
using Avalonia.VisualTree;
using GrpCurl.Net.Studio.Services;
using GrpCurl.Net.Studio.TestSupport;
using GrpCurl.Net.Studio.Tests.UI.Headless;
using GrpCurl.Net.Studio.ViewModels;
using GrpCurl.Net.Studio.ViewModels.Connections;
using GrpCurl.Net.Studio.ViewModels.Panes;
using GrpCurl.Net.Studio.ViewModels.Services;
using GrpCurl.Net.Studio.Views;

namespace GrpCurl.Net.Studio.Tests.UI;

/// <summary>
///     Accessibility guard (SPEC-020 §6): walks the realized visual tree of the shell and
///     fails if any interactive control lacks an accessible name. The effective name is read
///     via the control's automation peer, so a name supplied through content/header text
///     (e.g. a menu item's <c>Header</c>) counts — only genuinely unnamed controls fail.
/// </summary>
public sealed class AccessibilityTests(HeadlessSessionFixture fixture) : HeadlessTestBase(fixture)
{
    private static MainWindowViewModel CreateShellViewModel()
        => new(
            new InMemorySettingsStore(),
            new ConnectionsPaneViewModel(new FakeWorkspaceStore(), new FakeConnectionRegistry(), new FakeDialogService(), new ConnectionSelection()),
            new ServiceExplorerViewModel(new FakeDescriptorService(), new ConnectionSelection(), new FakeClipboardService(), new ImmediateUiDispatcher()),
            new ConsoleViewModel(),
            new InspectorViewModel());

    [Fact]
    public Task Every_interactive_control_in_the_shell_has_an_accessible_name() => RunOnUiThread(() =>
    {
        var window = new MainWindow { DataContext = CreateShellViewModel() };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var unnamed = window.GetVisualDescendants()
            .OfType<Control>()
            .Where(IsInteractive)
            .Where(control => string.IsNullOrWhiteSpace(EffectiveName(control)))
            .Select(Describe)
            .ToList();

        unnamed.ShouldBeEmpty("interactive controls without an accessible name: " + string.Join(", ", unnamed));
    });

    [Fact]
    public Task Every_interactive_control_in_the_connection_editor_has_an_accessible_name() => RunOnUiThread(() =>
    {
        var editor = new ConnectionEditorViewModel(new FakeConnectionRegistry());
        editor.AddHeaderCommand.Execute(null); // realize a header row's controls too

        var window = new Window { Content = new Views.Connections.ConnectionEditorView { DataContext = editor }, DataContext = editor };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var unnamed = window.GetVisualDescendants()
            .OfType<Control>()
            .Where(IsInteractive)
            .Where(control => string.IsNullOrWhiteSpace(EffectiveName(control)))
            .Select(Describe)
            .ToList();

        unnamed.ShouldBeEmpty("editor controls without an accessible name: " + string.Join(", ", unnamed));
    });

    private static bool IsInteractive(Control control) => control switch
    {
        // Templated sub-parts of a named control should not be required to carry their own
        // name; only the user-facing input controls are checked.
        Button or MenuItem or ToggleButton or CheckBox or RadioButton or TextBox or ComboBox or Slider => true,
        _ => false
    };

    private static string? EffectiveName(Control control)
        => ControlAutomationPeer.CreatePeerForElement(control)?.GetName();

    private static string Describe(Control control)
        => $"{control.GetType().Name}#{control.Name ?? "(anonymous)"}";
}
