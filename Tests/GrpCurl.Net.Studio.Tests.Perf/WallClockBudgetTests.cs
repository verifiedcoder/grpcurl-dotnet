using GrpCurl.Net.Studio.Services;
using GrpCurl.Net.Studio.TestSupport;
using GrpCurl.Net.Studio.ViewModels.Documents;
using GrpCurl.Net.Studio.ViewModels.Models.Connections;

namespace GrpCurl.Net.Studio.Tests.Perf;

/// <summary>
///     Wall-clock budget checks (SPEC-060 §2). These carry the spec's 25% headroom and are noise-prone on
///     shared runners, so they are tagged <c>Category=Performance</c> and run only in the nightly/dispatch
///     perf lane — the PR test job excludes this trait. They guard the CPU/in-memory budgets that don't need
///     a server; the app-level budgets (cold start, streaming throughput, keystroke latency) are tracked in A3.4.
/// </summary>
public sealed class WallClockBudgetTests
{
    // NFR-P9: JSON pretty-print of a ~1 MB body < 300 ms (on a background thread in the app; here we measure
    // the formatter itself, which is the cost that must fit the budget).
    [Fact]
    [Trait("Category", "Performance")]
    public void Pretty_printing_a_1MB_body_meets_NFR_P9()
    {
        var json = OneMegabyteJson();
        json.Length.ShouldBeGreaterThan(1_000_000); // the body really is ≥ 1 MB

        var p95 = PerfMeasure.P95Millis(runs: 20, warmup: 3, () => JsonText.TryPrettyPrint(json, out _));

        p95.ShouldBeLessThan(300 * PerfMeasure.Headroom, $"1 MB pretty-print p95 {p95:0} ms (budget 300 ms +25%)");
    }

    // NFR-P10: filtered history search < 100 ms over 1000 entries (in-memory index per ADR-008).
    [Fact]
    [Trait("Category", "Performance")]
    public void History_search_over_1000_entries_meets_NFR_P10()
    {
        var vm = LoadedHistory(1000);
        var terms = new[] { "Method01", "Method42", "Method73", "perf", "Service0001" };
        var next = 0;

        // Each assignment changes the value, so OnSearchTextChanged → ApplyFilter recomputes the rows.
        var p95 = PerfMeasure.P95Millis(runs: 20, warmup: 3, () => vm.SearchText = terms[next++ % terms.Length]);

        p95.ShouldBeLessThan(100 * PerfMeasure.Headroom, $"history search p95 {p95:0} ms (budget 100 ms +25%)");
    }

    // NFR-P11: workspace load < 500 ms for a workspace with 100 saved requests (file read + deserialize).
    [Fact]
    [Trait("Category", "Performance")]
    public void Workspace_load_for_100_requests_meets_NFR_P11()
    {
        var dir = Directory.CreateTempSubdirectory("grpcn-perf");

        try
        {
            var path = Path.Combine(dir.FullName, "workspace.gcnws.json");
            var store = new JsonWorkspaceStore(path, autosaveDebounce: TimeSpan.Zero);
            store.SaveAsAsync(PerfFixtures.SyntheticWorkspace(100), path).GetAwaiter().GetResult();

            var p95 = PerfMeasure.P95Millis(runs: 20, warmup: 3, () => store.ReadAsync(path).GetAwaiter().GetResult());

            p95.ShouldBeLessThan(500 * PerfMeasure.Headroom, $"workspace load p95 {p95:0} ms (budget 500 ms +25%)");
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    private static HistoryDocumentViewModel LoadedHistory(int count)
    {
        var store = new FakeHistoryStore();
        foreach (var entry in PerfFixtures.SyntheticHistory(count, DateTimeOffset.UnixEpoch))
        {
            store.Entries.Add(entry);
        }

        var vm = new HistoryDocumentViewModel(
            store, new InMemorySettingsStore(),
            new FakeWorkspaceStore(new WorkspaceModel { Connections = [new SavedConnection { Name = "staging", Address = "h:1" }] }),
            new FakeDocumentHost(), new FakeDialogService(), new ImmediateUiDispatcher(), new FakeFilePickerService());
        vm.LoadAsync().GetAwaiter().GetResult();
        return vm;
    }

    private static string OneMegabyteJson()
    {
        var items = Enumerable.Range(0, 20_000).Select(i =>
            $"{{\"id\":{i},\"name\":\"item-{i:D6}\",\"value\":{i * 7},\"active\":{(i % 2 == 0 ? "true" : "false")}}}");
        return "[" + string.Join(",", items) + "]";
    }
}
