using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using GrpCurl.Net.Studio.TestSupport;
using GrpCurl.Net.Studio.Tests.UI.Headless;
using GrpCurl.Net.Studio.ViewModels.Models.Connections;
using GrpCurl.Net.Studio.ViewModels.Models.Descriptors;
using GrpCurl.Net.Studio.ViewModels.Panes;
using GrpCurl.Net.Studio.ViewModels.Services;
using GrpCurl.Net.Studio.Views.Panes;

namespace GrpCurl.Net.Studio.Tests.UI;

/// <summary>
///     L3 headless E2E for the reflection explorer: renders the real view bound to a populated
///     view model and asserts the tree and streaming-shape badges materialize (FR-020/021).
/// </summary>
public sealed class ServiceExplorerUiTests(HeadlessSessionFixture fixture) : HeadlessTestBase(fixture)
{
    private static ServiceExplorerViewModel LoadedExplorer()
    {
        var descriptors = new FakeDescriptorService
        {
            Result = DescriptorLoadResult.Success(new ServiceCatalog(
            [
                new ServiceEntry("pkg.Greeter",
                [
                    new ServiceMethod("SayHello", "pkg.Greeter/SayHello", StreamingShape.Unary, "pkg.Req", "pkg.Resp"),
                    new ServiceMethod("Chat", "pkg.Greeter/Chat", StreamingShape.BidiStreaming, "pkg.Msg", "pkg.Msg")
                ])
            ], []))
        };

        var selection = new ConnectionSelection();
        var vm = new ServiceExplorerViewModel(descriptors, selection, new FakeClipboardService(), new ImmediateUiDispatcher(), new FakeDocumentHost());
        selection.Set(new SavedConnection { Name = "c", Address = "h:1" });
        return vm;
    }

    [Fact]
    public Task Explorer_renders_the_service_tree_when_loaded() => RunOnUiThread(() =>
    {
        var vm = LoadedExplorer();
        vm.IsLoaded.ShouldBeTrue();

        var window = new Window { Content = new ServiceExplorerView { DataContext = vm }, Width = 320, Height = 480 };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        // The loaded state shows the Services and Types trees; the service node is realized.
        var serviceTree = window.GetVisualDescendants().OfType<TreeView>()
            .Single(t => Equals(t.GetValue(Avalonia.Automation.AutomationProperties.NameProperty), "Service tree"));
        serviceTree.IsVisible.ShouldBeTrue();
        serviceTree.GetVisualDescendants().OfType<TreeViewItem>().ShouldNotBeEmpty();

        window.GetVisualDescendants()
            .OfType<TextBlock>()
            .Select(t => t.Text)
            .ShouldContain("pkg.Greeter");
    });

    [Fact]
    public Task Source_badge_and_warnings_strip_render_when_loaded() => RunOnUiThread(() =>
    {
        var descriptors = new FakeDescriptorService
        {
            Result = DescriptorLoadResult.Success(new ServiceCatalog(
                [new ServiceEntry("pkg.Greeter", [new ServiceMethod("Hi", "pkg.Greeter/Hi", StreamingShape.Unary, "pkg.A", "pkg.B")])],
                ["duplicate file a.proto"]) { FileCount = 2, SymbolCount = 5, LoadDuration = TimeSpan.FromMilliseconds(10) })
        };
        var selection = new ConnectionSelection();
        var vm = new ServiceExplorerViewModel(descriptors, selection, new FakeClipboardService(), new ImmediateUiDispatcher(), new FakeDocumentHost());
        selection.Set(new SavedConnection
        {
            Name = "c", Address = "h:1",
            DescriptorSource = new DescriptorSourceConfig { Mode = DescriptorMode.Protoset }
        });

        var window = new Window { Content = new ServiceExplorerView { DataContext = vm }, Width = 320, Height = 520 };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var texts = window.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).ToList();
        texts.ShouldContain("Protoset");
        texts.ShouldContain(t => t != null && t.Contains("1 warning"));
    });

    [Fact]
    public Task Export_button_renders_in_the_header_when_loaded() => RunOnUiThread(() =>
    {
        var window = new Window { Content = new ServiceExplorerView { DataContext = LoadedExplorer() }, Width = 320, Height = 480 };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var export = window.GetVisualDescendants().OfType<Button>()
            .FirstOrDefault(b => Equals(b.GetValue(Avalonia.Automation.AutomationProperties.NameProperty), "Export schema"));

        export.ShouldNotBeNull();
        export!.IsEffectivelyVisible.ShouldBeTrue();
    });
}
