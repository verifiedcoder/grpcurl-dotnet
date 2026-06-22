using Avalonia.Controls;
using Avalonia.Threading;
using GrpCurl.Net.Studio.Services;
using GrpCurl.Net.Studio.TestSupport;
using GrpCurl.Net.Studio.Tests.Perf.Headless;
using GrpCurl.Net.Studio.ViewModels.Documents;
using GrpCurl.Net.Studio.ViewModels.Models.Connections;
using GrpCurl.Net.Studio.Views.Documents;

namespace GrpCurl.Net.Studio.Tests.Perf;

/// <summary>
///     NFR-S7 / NFR-P12: the response viewer drops syntax highlighting (a whole-document tokenize pass) above
///     a size threshold so a multi-megabyte body renders without stalling the UI thread, and restores it for
///     normal-sized bodies. Guards the downgrade switch behaviourally; the wall-clock keystroke latency itself
///     is a desktop measurement (charter "editor latency" check).
/// </summary>
public sealed class LargeResponseTests(HeadlessSessionFixture fixture) : HeadlessTestBase(fixture)
{
    [Fact]
    [Trait("Category", "PerfBehavioural")]
    public Task Response_viewer_drops_highlighting_above_the_threshold_and_restores_it() => RunOnUiThread(() =>
    {
        var docs = CreateDocuments();
        docs.OpenInvocation(new SavedConnection { Name = "c", Address = "h:1" }, "perf.v1.Service0001/Method00", "{}");
        var vm = docs.Documents.OfType<InvocationDocumentViewModel>().Single();

        var view = new InvocationDocumentView { DataContext = vm };
        var window = new Window { Content = view, Width = 800, Height = 600 };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        view.ResponseHighlightingEnabled.ShouldBeTrue("a small/empty response keeps highlighting");

        // A response well over the 4 MiB threshold: highlighting must drop.
        vm.ResponseJson = new string('a', (4 * 1024 * 1024) + 16);
        Dispatcher.UIThread.RunJobs();
        view.ResponseHighlightingEnabled.ShouldBeFalse("an oversized response drops highlighting");

        // A subsequent normal response gets colour back.
        vm.ResponseJson = "{\n  \"ok\": true\n}";
        Dispatcher.UIThread.RunJobs();
        view.ResponseHighlightingEnabled.ShouldBeTrue("a normal response restores highlighting");
    });

    private static DocumentsViewModel CreateDocuments()
        => new(
            new FakeDescriptorService(), new ImmediateUiDispatcher(), new FakeClipboardService(), new FakeInvocationRunner(),
            new FakeDialogService(), new FakeLauncherService(), new FakeRequestValidator(),
            new InMemorySettingsStore(), new FakeThemeService());
}
