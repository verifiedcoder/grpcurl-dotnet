using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Threading;
using Avalonia.VisualTree;
using GrpCurl.Net.Studio.Tests.UI.Headless;
using GrpCurl.Net.Studio.TestSupport;
using GrpCurl.Net.Studio.ViewModels.Connections;
using GrpCurl.Net.Studio.ViewModels.Models.Connections;

namespace GrpCurl.Net.Studio.Tests.UI;

/// <summary>
///     L3 headless render tests for the connection editor's descriptor-source section (E2.3 PR-B): the
///     protoset / proto sub-panels realize with the selected mode, and every interactive control carries
///     an accessible name (SPEC-020 §6).
/// </summary>
public sealed class ConnectionEditorDescriptorUiTests(HeadlessSessionFixture fixture) : HeadlessTestBase(fixture)
{
    private static ConnectionEditorViewModel ProtoEditor()
    {
        var existing = new SavedConnection
        {
            Name = "c",
            Address = "localhost:443",
            DescriptorSource = new DescriptorSourceConfig
            {
                Mode = DescriptorMode.Proto,
                ProtoFiles = ["a.proto"],
                ImportPaths = ["/inc"]
            }
        };

        return new ConnectionEditorViewModel(
            new FakeConnectionRegistry(), existing, networkDefaults: null,
            profileStore: null, new FakeFilePickerService(), dialogService: null, secretStore: null, new FakeProtocService());
    }

    private static Window Render(ConnectionEditorViewModel vm)
    {
        var window = new Window { Content = new Views.Connections.ConnectionEditorView { DataContext = vm }, DataContext = vm };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return window;
    }

    private static Control? ByName(Visual root, string name) =>
        root.GetVisualDescendants().OfType<Control>()
            .FirstOrDefault(c => Equals(c.GetValue(AutomationProperties.NameProperty), name));

    [Fact]
    public Task Proto_mode_reveals_proto_and_import_controls_and_hides_protoset() => RunOnUiThread(() =>
    {
        var window = Render(ProtoEditor());

        ByName(window, "Add proto files")!.IsEffectivelyVisible.ShouldBeTrue();
        ByName(window, "Add import directory")!.IsEffectivelyVisible.ShouldBeTrue();
        ByName(window, "Add protosets")!.IsEffectivelyVisible.ShouldBeFalse();
    });

    [Fact]
    public Task Protoset_mode_reveals_the_protoset_list() => RunOnUiThread(() =>
    {
        var vm = ProtoEditor();
        vm.SelectedDescriptorMode = DescriptorMode.Protoset;
        var window = Render(vm);

        ByName(window, "Add protosets")!.IsEffectivelyVisible.ShouldBeTrue();
        ByName(window, "Add proto files")!.IsEffectivelyVisible.ShouldBeFalse();
    });

    [Fact]
    public Task Every_interactive_control_has_an_accessible_name() => RunOnUiThread(() =>
    {
        var window = Render(ProtoEditor());

        var unnamed = window.GetVisualDescendants()
            .OfType<Control>()
            .Where(c => c is Button or ToggleButton or CheckBox or TextBox or ComboBox)
            .Where(c => c.TemplatedParent is null)
            .Where(c => string.IsNullOrWhiteSpace(ControlAutomationPeer.CreatePeerForElement(c)?.GetName()))
            .Select(c => c.GetType().Name)
            .ToList();

        unnamed.ShouldBeEmpty("unnamed: " + string.Join(", ", unnamed));
    });
}
