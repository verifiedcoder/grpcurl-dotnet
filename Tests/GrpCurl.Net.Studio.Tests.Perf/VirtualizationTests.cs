using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using GrpCurl.Net.Studio.Services;
using GrpCurl.Net.Studio.TestSupport;
using GrpCurl.Net.Studio.Tests.Perf.Headless;
using GrpCurl.Net.Studio.ViewModels.Documents;
using GrpCurl.Net.Studio.ViewModels.Models.Connections;
using GrpCurl.Net.Studio.ViewModels.Models.Descriptors;
using GrpCurl.Net.Studio.ViewModels.Panes;
using GrpCurl.Net.Studio.ViewModels.Services;
using GrpCurl.Net.Studio.Views.Documents;
using GrpCurl.Net.Studio.Views.Panes;

namespace GrpCurl.Net.Studio.Tests.Perf;

/// <summary>
///     Rendered virtualization guards (V-HEADLESS, PR-gated). Underpins NFR-P4/P5/P6: the explorer tree and
///     the history list must realize only the containers near the viewport, not one per item, so a
///     500-service server or a 10 000-row history stays cheap to render and light on memory regardless of size.
/// </summary>
public sealed class VirtualizationTests(HeadlessSessionFixture fixture) : HeadlessTestBase(fixture)
{
    [Fact]
    [Trait("Category", "PerfBehavioural")]
    public Task Service_tree_virtualizes_a_500_service_catalog() => RunOnUiThread(() =>
    {
        var catalog = PerfFixtures.SyntheticCatalog(PerfFixtures.LargeServiceCount, methodsPerService: 8);
        var descriptors = new FakeDescriptorService { Result = DescriptorLoadResult.Success(catalog) };
        var selection = new ConnectionSelection();
        var vm = new ServiceExplorerViewModel(
            descriptors, selection, new FakeClipboardService(), new ImmediateUiDispatcher(), new FakeDocumentHost());
        selection.Set(new SavedConnection { Name = "c", Address = "h:1" });
        vm.Services.Count.ShouldBe(PerfFixtures.LargeServiceCount); // sanity: the data is all there

        // A realistically-sized pane so only a fraction of the 500 rows fit the viewport.
        var window = new Window { Content = new ServiceExplorerView { DataContext = vm }, Width = 320, Height = 600 };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var serviceTree = window.GetVisualDescendants().OfType<TreeView>()
            .Single(t => Equals(t.GetValue(AutomationProperties.NameProperty), "Service tree"));

        // Virtualized: realized containers track the viewport, not the item count. A non-virtualized tree
        // would materialize all 500. The bound is generous to stay robust across headless layout sizing.
        var realized = serviceTree.GetVisualDescendants().OfType<TreeViewItem>().Count();
        realized.ShouldBeLessThan(150, $"service tree realized {realized} of {PerfFixtures.LargeServiceCount} containers");
        realized.ShouldBeGreaterThan(0, "the tree rendered no rows at all");
    });

    [Fact]
    [Trait("Category", "PerfBehavioural")]
    public Task History_list_virtualizes_a_large_history() => RunOnUiThread(() =>
    {
        const int rows = 5000;
        var store = new FakeHistoryStore();
        foreach (var entry in PerfFixtures.SyntheticHistory(rows, DateTimeOffset.UnixEpoch))
        {
            store.Entries.Add(entry);
        }

        var vm = new HistoryDocumentViewModel(
            store, new InMemorySettingsStore(),
            new FakeWorkspaceStore(new WorkspaceModel { Connections = [new SavedConnection { Name = "staging", Address = "h:1" }] }),
            new FakeDocumentHost(), new FakeDialogService(), new ImmediateUiDispatcher(), new FakeFilePickerService());
        vm.LoadAsync().GetAwaiter().GetResult();

        var window = new Window { Content = new HistoryDocumentView { DataContext = vm }, Width = 900, Height = 600 };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var list = window.GetVisualDescendants().OfType<ListBox>()
            .Single(l => Equals(l.GetValue(AutomationProperties.NameProperty), "History entries"));

        var realized = list.GetVisualDescendants().OfType<ListBoxItem>().Count();
        realized.ShouldBeLessThan(150, $"history list realized {realized} of {rows} rows");
        realized.ShouldBeGreaterThan(0, "the list rendered no rows at all");
    });
}
