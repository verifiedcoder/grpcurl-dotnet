using GrpCurl.Net.Studio.Tests.Unit.Fakes;
using GrpCurl.Net.Studio.TestSupport;
using GrpCurl.Net.Studio.ViewModels.Documents;
using GrpCurl.Net.Studio.ViewModels.Models.Connections;
using GrpCurl.Net.Studio.ViewModels.Models.History;

namespace GrpCurl.Net.Studio.Tests.Unit;

/// <summary>E3.3 PR-C: the History tab — load/filter, pin, clear, export, and replay-to-tab.</summary>
public sealed class HistoryDocumentViewModelTests
{
    private static HistoryEntry Entry(
        string id, string connection = "staging", string method = "pkg.Svc/Go", string category = "success",
        HistoryKind kind = HistoryKind.Grpc, bool pinned = false, bool truncated = false, string body = "{}") => new(
        HistoryEntry.CurrentVersion, id, new DateTimeOffset(2026, 6, 11, 14, 0, int.Parse(id[^1].ToString()), TimeSpan.Zero), kind,
        new HistoryConnection(connection, "h:1", "tls", null), "/ws/x.gcnws.json", method,
        new HistoryRequest("json", body, truncated, [], "10s", false, false, null, null, null),
        new HistoryOutcome(category == "success" ? "OK" : "NotFound", category, category == "success" ? 0 : 69, 12, 1, 1, null, false, null),
        pinned);

    private static HistoryDocumentViewModel Create(
        out FakeHistoryStore store, out FakeDocumentHost host, out FakeDialogService dialogs,
        out FakeFilePickerService picker, params HistoryEntry[] entries)
    {
        store = new FakeHistoryStore();
        store.Entries.AddRange(entries);
        host = new FakeDocumentHost();
        dialogs = new FakeDialogService();
        picker = new FakeFilePickerService();
        var settings = new FakeSettingsStore();
        var workspace = new FakeWorkspaceStore(new WorkspaceModel
        {
            Connections = [new SavedConnection { Name = "staging", Address = "h:1" }]
        });
        var doc = new HistoryDocumentViewModel(store, settings, workspace, host, dialogs, new ImmediateUiDispatcher(), picker);
        doc.LoadAsync().GetAwaiter().GetResult();
        return doc;
    }

    [Fact]
    public void Loads_entries_newest_first()
    {
        var doc = Create(out _, out _, out _, out _, Entry("e1"), Entry("e2"), Entry("e3"));

        doc.Rows.Select(r => r.Id).ShouldBe(["e3", "e2", "e1"]);
        doc.Title.ShouldBe("History");
    }

    [Fact]
    public void Search_filters_by_method_connection_and_body()
    {
        var doc = Create(out _, out _, out _, out _,
            Entry("e1", method: "pkg.Svc/Alpha"), Entry("e2", method: "pkg.Svc/Beta", body: "{ \"needle\": 1 }"));

        doc.SearchText = "alpha";
        doc.Rows.ShouldHaveSingleItem().Id.ShouldBe("e1");

        doc.SearchText = "needle";
        doc.Rows.ShouldHaveSingleItem().Id.ShouldBe("e2");
    }

    [Fact]
    public void Category_and_pinned_filters_apply()
    {
        var doc = Create(out _, out _, out _, out _,
            Entry("e1", category: "success"), Entry("e2", category: "rpc-error", pinned: true));

        doc.CategoryFilter = "rpc-error";
        doc.Rows.ShouldHaveSingleItem().Id.ShouldBe("e2");

        doc.CategoryFilter = "All";
        doc.PinnedOnly = true;
        doc.Rows.ShouldHaveSingleItem().Id.ShouldBe("e2");
    }

    [Fact]
    public async Task Toggle_pin_updates_the_store()
    {
        var doc = Create(out var store, out _, out _, out _, Entry("e1"));

        await doc.TogglePinCommand.ExecuteAsync(doc.Rows[0]);

        store.Entries.Single(e => e.Id == "e1").Pinned.ShouldBeTrue();
    }

    [Fact]
    public async Task Replay_opens_an_invocation_tab_when_the_connection_resolves()
    {
        var doc = Create(out _, out var host, out _, out _, Entry("e1", connection: "staging", method: "pkg.Svc/Go", body: "{ \"a\": 1 }"));

        await doc.ReplayCommand.ExecuteAsync(doc.Rows[0]);

        var opened = host.LastPrefill.ShouldNotBeNull();
        opened.Symbol.ShouldBe("pkg.Svc/Go");
        opened.Prefill.Body.ShouldBe("{ \"a\": 1 }");
    }

    [Fact]
    public async Task Replay_restores_headers_and_options_with_a_marker_on_redacted_secrets()
    {
        var request = new HistoryRequest(
            "text", "{}", BodyTruncated: false,
            Headers:
            [
                new HistoryHeader("x-trace", "abc"),                       // plain value restored verbatim
                new HistoryHeader("authorization", HistoryEntry.RedactedMarker), // secret → needs re-entry
                new HistoryHeader("x-env", "${TOKEN}")                     // ${VAR} restored verbatim
            ],
            Deadline: "30s", EmitDefaults: true, AllowUnknownFields: false,
            MaxSendBytes: null, MaxReceiveBytes: 4096, EnvironmentName: null);
        var entry = new HistoryEntry(
            HistoryEntry.CurrentVersion, "e1", new DateTimeOffset(2026, 6, 11, 14, 0, 0, TimeSpan.Zero), HistoryKind.Grpc,
            new HistoryConnection("staging", "h:1", "tls", null), "/ws/x.gcnws.json", "pkg.Svc/Go", request,
            new HistoryOutcome("OK", "success", 0, 12, 1, 1, null, false, null));
        var doc = Create(out _, out var host, out _, out _, entry);

        await doc.ReplayCommand.ExecuteAsync(doc.Rows[0]);

        var prefill = host.LastPrefill.ShouldNotBeNull().Prefill;
        prefill.BodyFormat.ShouldBe(GrpCurl.Net.Studio.ViewModels.Models.Invocation.RequestBodyFormat.Text);
        prefill.Deadline.ShouldBe("30s");
        prefill.EmitDefaults.ShouldBeTrue();
        prefill.AllowUnknownFields.ShouldBeFalse();
        prefill.MaxMessageSize.ShouldBe("4096");

        prefill.Headers.Select(h => h.Name).ShouldBe(["x-trace", "authorization", "x-env"]);
        prefill.Headers[0].Value.ShouldBe("abc");
        prefill.Headers[0].RequiresValue.ShouldBeFalse();
        prefill.Headers[1].Value.ShouldBe(string.Empty);          // redacted secret blanked
        prefill.Headers[1].RequiresValue.ShouldBeTrue();          // FR-123 marker
        prefill.Headers[2].Value.ShouldBe("${TOKEN}");            // re-resolves at send time
    }

    [Fact]
    public async Task Replay_warns_when_the_connection_no_longer_exists()
    {
        var doc = Create(out _, out var host, out var dialogs, out _, Entry("e1", connection: "gone"));

        await doc.ReplayCommand.ExecuteAsync(doc.Rows[0]);

        dialogs.MessageCount.ShouldBe(1);
        host.Prefills.ShouldBeEmpty();
    }

    [Fact]
    public void A_truncated_entry_is_not_replayable()
    {
        var doc = Create(out _, out _, out _, out _, Entry("e1", connection: "staging", truncated: true));

        doc.Rows[0].Replayable.ShouldBeFalse();
    }

    [Fact]
    public async Task Delete_selected_removes_the_checked_rows()
    {
        var doc = Create(out var store, out _, out _, out _, Entry("e1"), Entry("e2"));
        doc.Rows.First(r => r.Id == "e1").IsSelected = true;

        await doc.DeleteSelectedCommand.ExecuteAsync(null);

        store.Entries.Select(e => e.Id).ShouldBe(["e2"]);
    }

    [Fact]
    public async Task Clear_all_confirms_then_empties()
    {
        var doc = Create(out var store, out _, out var dialogs, out _, Entry("e1", pinned: true), Entry("e2"));
        dialogs.ConfirmResult = true;

        await doc.ClearAllCommand.ExecuteAsync(null);

        dialogs.ConfirmCount.ShouldBe(1);
        store.Entries.ShouldBeEmpty();
    }

    [Fact]
    public async Task Export_writes_the_filtered_entries_to_the_picked_path()
    {
        var doc = Create(out var store, out _, out _, out var picker, Entry("e1"), Entry("e2"));
        picker.SaveResult = "/tmp/history.ndjson";

        await doc.ExportCommand.ExecuteAsync(null);

        store.ExportedPath.ShouldBe("/tmp/history.ndjson");
        store.ExportedEntries!.Count.ShouldBe(2);
    }

    [Fact]
    public async Task Capture_disabled_drives_the_banner_flag()
    {
        var store = new FakeHistoryStore();
        var settings = new FakeSettingsStore();
        settings.Current.History.Enabled = false;
        var doc = new HistoryDocumentViewModel(store, settings, new FakeWorkspaceStore(), new FakeDocumentHost(),
            new FakeDialogService(), new ImmediateUiDispatcher());

        await doc.LoadAsync();

        doc.CaptureEnabled.ShouldBeFalse();
    }
}
