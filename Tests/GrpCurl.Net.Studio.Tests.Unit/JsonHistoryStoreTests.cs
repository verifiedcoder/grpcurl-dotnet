using GrpCurl.Net.Studio.Services;
using GrpCurl.Net.Studio.ViewModels.Models.History;

namespace GrpCurl.Net.Studio.Tests.Unit;

/// <summary>E3.3 PR-A: the append-only NDJSON history store (SPEC-040 §5) — append/read, tolerant reads,
/// pin/delete/clear, and oldest-unpinned-first retention.</summary>
public sealed class JsonHistoryStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "grpcn-hist-" + Guid.NewGuid().ToString("N"));

    public JsonHistoryStoreTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, recursive: true);
        }
    }

    private string Path_ => Path.Combine(_dir, "history.ndjson");

    private static HistoryEntry Entry(string id, bool pinned = false, string method = "pkg.Svc/Go") => new(
        HistoryEntry.CurrentVersion, id, new DateTimeOffset(2026, 6, 11, 14, 3, 22, 114, TimeSpan.Zero), HistoryKind.Grpc,
        new HistoryConnection("staging", "api.example.com:443", "tls", "mtls"), "/ws/x.gcnws.json", method,
        new HistoryRequest("json", "{}", BodyTruncated: false,
            [new HistoryHeader("authorization", HistoryEntry.RedactedMarker), new HistoryHeader("x-trace", "abc")],
            "10s", EmitDefaults: false, AllowUnknownFields: false, MaxSendBytes: null, MaxReceiveBytes: null, EnvironmentName: null),
        new HistoryOutcome("OK", "success", 0, 184, 1, 1, ResponseBody: null, ResponseTruncated: false, ErrorMessage: null),
        pinned);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Append_then_read_round_trips_entries_in_file_order()
    {
        var store = new JsonHistoryStore(Path_);
        await store.AppendAsync(Entry("a", method: "pkg.Svc/First"), Ct);
        await store.AppendAsync(Entry("b", method: "pkg.Svc/Second"), Ct);

        var all = await store.ReadAllAsync(Ct);

        all.Select(e => e.Id).ShouldBe(["a", "b"]);   // oldest first
        all[0].Method.ShouldBe("pkg.Svc/First");
        all[0].Request.Headers.ShouldContain(h => h.Name == "authorization" && h.Value == HistoryEntry.RedactedMarker);
    }

    [Fact]
    public async Task Each_line_is_compact_camelcase_json_with_a_version()
    {
        var store = new JsonHistoryStore(Path_);
        await store.AppendAsync(Entry("a"), Ct);

        var line = (await File.ReadAllLinesAsync(Path_, Ct)).Single();

        line.ShouldStartWith("{");
        line.ShouldContain("\"v\":1");
        line.ShouldContain("\"kind\":\"grpc\"");
        line.ShouldContain("\"workspacePath\":");      // camelCase
        line.ShouldContain(HistoryEntry.RedactedMarker); // secret never present, only the marker
    }

    [Fact]
    public async Task A_truncated_tail_line_is_dropped_on_read()
    {
        var store = new JsonHistoryStore(Path_);
        await store.AppendAsync(Entry("a"), Ct);
        await File.AppendAllTextAsync(Path_, "{ half-written entry without a newl", Ct); // crash mid-append

        var all = await store.ReadAllAsync(Ct);

        all.ShouldHaveSingleItem().Id.ShouldBe("a");
    }

    [Fact]
    public async Task Pinning_updates_the_entry()
    {
        var store = new JsonHistoryStore(Path_);
        await store.AppendAsync(Entry("a"), Ct);

        await store.SetPinnedAsync("a", true, Ct);

        (await store.ReadAllAsync(Ct)).Single().Pinned.ShouldBeTrue();
    }

    [Fact]
    public async Task Delete_removes_the_named_entries()
    {
        var store = new JsonHistoryStore(Path_);
        await store.AppendAsync(Entry("a"), Ct);
        await store.AppendAsync(Entry("b"), Ct);
        await store.AppendAsync(Entry("c"), Ct);

        await store.DeleteAsync(["a", "c"], Ct);

        (await store.ReadAllAsync(Ct)).Select(e => e.Id).ShouldBe(["b"]);
    }

    [Fact]
    public async Task Clear_all_empties_the_history()
    {
        var store = new JsonHistoryStore(Path_);
        await store.AppendAsync(Entry("a"), Ct);

        await store.ClearAsync(keepPinned: false, Ct);

        (await store.ReadAllAsync(Ct)).ShouldBeEmpty();
    }

    [Fact]
    public async Task Clear_keeping_pinned_retains_only_pinned_entries()
    {
        var store = new JsonHistoryStore(Path_);
        await store.AppendAsync(Entry("a", pinned: true), Ct);
        await store.AppendAsync(Entry("b"), Ct);

        await store.ClearAsync(keepPinned: true, Ct);

        (await store.ReadAllAsync(Ct)).Select(e => e.Id).ShouldBe(["a"]);
    }

    [Fact]
    public async Task Retention_evicts_oldest_first_when_over_the_entry_cap()
    {
        var store = new JsonHistoryStore(Path_, maxEntries: 3);

        for (var i = 0; i < 5; i++)
        {
            await store.AppendAsync(Entry($"e{i}"), Ct);
        }

        (await store.ReadAllAsync(Ct)).Select(e => e.Id).ShouldBe(["e2", "e3", "e4"]); // newest 3
    }

    [Fact]
    public async Task Retention_spares_pinned_entries_and_evicts_the_oldest_unpinned()
    {
        var store = new JsonHistoryStore(Path_, maxEntries: 2);
        await store.AppendAsync(Entry("old-pinned", pinned: true), Ct);
        await store.AppendAsync(Entry("e1"), Ct);
        await store.AppendAsync(Entry("e2"), Ct); // over cap → evict oldest UNPINNED (e1), keep the pin

        (await store.ReadAllAsync(Ct)).Select(e => e.Id).ShouldBe(["old-pinned", "e2"]);
    }

    [Fact]
    public async Task Retention_follows_the_live_max_entries_setting()
    {
        // FR-158: the cap comes from settings, so changing it changes eviction without rebuilding the store.
        var settings = new GrpCurl.Net.Studio.Tests.Unit.Fakes.FakeSettingsStore();
        settings.Current.History.MaxEntries = 2;
        settings.Current.History.MaxBytes = long.MaxValue; // isolate the entry-count cap
        var store = new JsonHistoryStore(Path_, settings);

        await store.AppendAsync(Entry("e1"), Ct);
        await store.AppendAsync(Entry("e2"), Ct);
        await store.AppendAsync(Entry("e3"), Ct);

        (await store.ReadAllAsync(Ct)).Select(e => e.Id).ShouldBe(["e2", "e3"]); // honours the setting

        settings.Current.History.MaxEntries = 1;
        await store.AppendAsync(Entry("e4"), Ct);

        (await store.ReadAllAsync(Ct)).Select(e => e.Id).ShouldBe(["e4"]); // tighter cap applied live
    }

    [Fact]
    public async Task Repeated_reads_are_served_from_cache_until_the_file_changes(/* #5 */)
    {
        var store = new JsonHistoryStore(Path_);
        await store.AppendAsync(Entry("e1"), Ct);

        var first = await store.ReadAllAsync(Ct);
        var second = await store.ReadAllAsync(Ct);
        second.ShouldBeSameAs(first); // unchanged file → cached parse reused

        await store.AppendAsync(Entry("e2"), Ct);
        var third = await store.ReadAllAsync(Ct);
        third.ShouldNotBeSameAs(first); // the write invalidated the cache
        third.Select(e => e.Id).ShouldBe(["e1", "e2"]);
    }

    [Fact]
    public async Task An_external_write_is_picked_up_by_the_signature_check(/* #5 */)
    {
        var store = new JsonHistoryStore(Path_);
        await store.AppendAsync(Entry("e1"), Ct);
        (await store.ReadAllAsync(Ct)).Count.ShouldBe(1); // warm the cache

        // Simulate another instance appending a line directly to the file.
        await File.AppendAllTextAsync(Path_,
            System.Text.Json.JsonSerializer.Serialize(Entry("e2"), GrpCurl.Net.Studio.Services.HistoryJsonContext.Default.HistoryEntry) + "\n", Ct);

        (await store.ReadAllAsync(Ct)).Select(e => e.Id).ShouldBe(["e1", "e2"]); // re-read, not stale cache
    }
}
