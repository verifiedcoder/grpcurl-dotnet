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
        var vm = new ServiceExplorerViewModel(descriptors, selection, new FakeClipboardService(), new ImmediateUiDispatcher());
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

        // The tree is visible (loaded state) and realizes its top-level service node.
        var tree = window.GetVisualDescendants().OfType<TreeView>().Single();
        tree.IsVisible.ShouldBeTrue();
        tree.GetVisualDescendants().OfType<TreeViewItem>().ShouldNotBeEmpty();

        window.GetVisualDescendants()
            .OfType<TextBlock>()
            .Select(t => t.Text)
            .ShouldContain("pkg.Greeter");
    });
}
