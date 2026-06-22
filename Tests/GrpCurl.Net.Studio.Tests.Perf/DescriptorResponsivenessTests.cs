using GrpCurl.Net.Studio.Services;
using GrpCurl.Net.Studio.TestSupport;
using GrpCurl.Net.Studio.ViewModels.Documents;
using GrpCurl.Net.Studio.ViewModels.Models.Connections;
using GrpCurl.Net.Studio.ViewModels.Models.Descriptors;
using GrpCurl.Net.Studio.ViewModels.Panes;
using GrpCurl.Net.Studio.ViewModels.Services;

namespace GrpCurl.Net.Studio.Tests.Perf;

/// <summary>
///     Behavioural perf guards (V-HEADLESS lane, PR-gated). These assert the responsiveness <em>contract</em>
///     behind the SPEC-060 budgets — work is the right shape and never blocks the UI thread — rather than
///     wall-clock numbers, so they are deterministic on shared CI runners. The wall-clock budgets
///     (NFR-P1/P5/… with their timings) run in the nightly Performance lane added in A3.3.
/// </summary>
public sealed class DescriptorResponsivenessTests
{
    private static SavedConnection Conn() => new() { Name = "c", Address = "h:1" };

    // NFR-P5 (data path): the explorer materialises a 500-service catalog in full. A3.2 adds the
    // virtualization assertion (only a bounded number of tree containers realized for this same catalog).
    [Fact]
    [Trait("Category", "PerfBehavioural")]
    public void Explorer_materialises_the_500_service_reference_catalog()
    {
        var catalog = PerfFixtures.SyntheticCatalog(PerfFixtures.LargeServiceCount, methodsPerService: 8);
        var descriptors = new FakeDescriptorService { Result = DescriptorLoadResult.Success(catalog) };
        var selection = new ConnectionSelection();
        var vm = new ServiceExplorerViewModel(
            descriptors, selection, new FakeClipboardService(), new ImmediateUiDispatcher(), new FakeDocumentHost());

        selection.Set(Conn());

        vm.IsLoaded.ShouldBeTrue();
        vm.Services.Count.ShouldBe(PerfFixtures.LargeServiceCount);
        vm.Services.Sum(s => s.Methods.Count).ShouldBe(PerfFixtures.LargeServiceCount * 8);
    }

    // NFR-P7 / NFR-P8: opening a tab must not synchronously wait on descriptor resolution. The descriptor
    // source here never returns; if the open path awaited it the call would hang, failing the test.
    [Fact(Timeout = 5000)]
    [Trait("Category", "PerfBehavioural")]
    public void Opening_an_invocation_tab_never_blocks_on_descriptor_resolution()
    {
        var stuck = new TaskCompletionSource<DescribeResult>();
        var descriptors = new FakeDescriptorService { OnDescribe = (_, _, _) => stuck.Task };
        var docs = CreateDocuments(descriptors);

        docs.OpenInvocation(Conn(), "perf.v1.Service0001/Method00");

        // The tab is open and interactive even though the descriptor never resolved.
        _ = docs.Documents.ShouldHaveSingleItem().ShouldBeOfType<InvocationDocumentViewModel>();
    }

    private static DocumentsViewModel CreateDocuments(FakeDescriptorService descriptors)
        => new(
            descriptors, new ImmediateUiDispatcher(), new FakeClipboardService(), new FakeInvocationRunner(),
            new FakeDialogService(), new FakeLauncherService(), new FakeRequestValidator(),
            new InMemorySettingsStore(), new FakeThemeService());
}
