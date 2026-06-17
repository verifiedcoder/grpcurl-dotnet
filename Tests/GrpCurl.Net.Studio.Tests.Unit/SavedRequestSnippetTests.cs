using GrpCurl.Net.Studio.Services;
using GrpCurl.Net.Studio.TestSupport;
using GrpCurl.Net.Studio.ViewModels.Connections;
using GrpCurl.Net.Studio.ViewModels.Models.Connections;
using GrpCurl.Net.Studio.ViewModels.Models.Invocation;
using GrpCurl.Net.Studio.ViewModels.Panes;
using GrpCurl.Net.Studio.ViewModels.Services;

namespace GrpCurl.Net.Studio.Tests.Unit;

/// <summary>FR-166: single saved-request snippet export/import — the IO round-trip and the sidebar actions.</summary>
public sealed class SavedRequestSnippetTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "grpcn-snippet-" + Guid.NewGuid().ToString("N"));

    private CancellationToken Ct => TestContext.Current.CancellationToken;

    public SavedRequestSnippetTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private static SavedRequest Sample() => new()
    {
        Id = "r1", Name = "say hello", ConnectionId = "c1", Method = "demo.Greeter/SayHello",
        BodyFormat = RequestBodyFormat.Text, Body = "{ \"name\": \"world\" }",
        Headers = [new HeaderEntry { Name = "authorization", Value = "${TOKEN}" }],
        Deadline = "10s", EmitDefaults = true, MaxReceiveBytes = 2048
    };

    // ── IO round-trip (SavedRequestSnippetIO) ────────────────────────────────

    [Fact]
    public async Task A_request_round_trips_through_a_snippet_file()
    {
        var io = new SavedRequestSnippetIO();
        var path = Path.Combine(_dir, "req.grpcnreq.json");

        await io.ExportAsync(Sample(), path, Ct);
        var imported = await io.ImportAsync(path, Ct);

        imported.Name.ShouldBe("say hello");
        imported.Method.ShouldBe("demo.Greeter/SayHello");
        imported.BodyFormat.ShouldBe(RequestBodyFormat.Text);
        imported.Headers.ShouldHaveSingleItem().Value.ShouldBe("${TOKEN}");
        imported.MaxReceiveBytes.ShouldBe(2048);
    }

    [Fact]
    public async Task The_snippet_file_is_a_kind_tagged_envelope()
    {
        var io = new SavedRequestSnippetIO();
        var path = Path.Combine(_dir, "req.grpcnreq.json");
        await io.ExportAsync(Sample(), path, Ct);

        var json = await File.ReadAllTextAsync(path, Ct);

        json.ShouldContain("\"kind\": \"grpcn.savedRequest\"");
        json.ShouldContain("\"bodyFormat\": \"text\"");
        json.ShouldNotContain("\r"); // LF endings like the workspace file
    }

    [Fact]
    public async Task Importing_a_non_snippet_file_throws_a_friendly_error()
    {
        var io = new SavedRequestSnippetIO();
        var path = Path.Combine(_dir, "junk.json");
        await File.WriteAllTextAsync(path, "{ \"hello\": 1 }", Ct);

        var ex = await Should.ThrowAsync<SavedRequestSnippetException>(() => io.ImportAsync(path, Ct));
        ex.Message.ShouldContain("not a saved-request snippet");
    }

    [Fact]
    public async Task Importing_corrupt_json_throws_a_friendly_error()
    {
        var io = new SavedRequestSnippetIO();
        var path = Path.Combine(_dir, "broken.json");
        await File.WriteAllTextAsync(path, "{ not json", Ct);

        await Should.ThrowAsync<SavedRequestSnippetException>(() => io.ImportAsync(path, Ct));
    }

    // ── sidebar export (SavedRequestItemViewModel) ───────────────────────────

    [Fact]
    public async Task Exporting_a_request_writes_it_through_the_snippet_io()
    {
        var snippetIO = new FakeSavedRequestSnippetIO();
        var picker = new FakeFilePickerService { SaveResult = "/out/hello.grpcnreq.json" };
        var item = new SavedRequestItemViewModel(
            Sample(), _ => Task.CompletedTask, store: null, dialogs: new FakeDialogService(), picker, snippetIO);
        item.CanExport.ShouldBeTrue();

        await item.ExportCommand.ExecuteAsync(null);

        snippetIO.LastExport.ShouldNotBeNull();
        snippetIO.LastExport!.Value.Path.ShouldBe("/out/hello.grpcnreq.json");
        snippetIO.LastExport.Value.Request.Name.ShouldBe("say hello");
        picker.LastSaveSuggestedName.ShouldBe("say hello.grpcnreq.json");
    }

    // ── sidebar import (ConnectionsPaneViewModel) ────────────────────────────

    private static ConnectionsPaneViewModel Pane(
        out FakeSavedRequestSnippetIO snippetIO, out FakeFilePickerService picker, out SavedRequestStore store,
        out FakeDialogService dialogs, WorkspaceModel workspace)
    {
        snippetIO = new FakeSavedRequestSnippetIO();
        picker = new FakeFilePickerService();
        dialogs = new FakeDialogService();
        var ws = new FakeWorkspaceStore(workspace);
        store = new SavedRequestStore(ws);
        return new ConnectionsPaneViewModel(
            ws, new FakeConnectionRegistry(), dialogs, new ConnectionSelection(),
            filePicker: picker, savedRequests: store, documentHost: new FakeDocumentHost(), snippetIO: snippetIO);
    }

    [Fact]
    public async Task Importing_a_snippet_rebinds_it_to_the_target_connection()
    {
        var pane = Pane(out var snippetIO, out var picker, out var store, out _,
            new WorkspaceModel { Connections = [new SavedConnection { Id = "c1", Name = "alpha", Address = "h:1" }] });
        picker.OpenResult = "/in/req.grpcnreq.json";
        snippetIO.ImportResult = new SavedRequest { Id = "other", Name = "hello", ConnectionId = "source", Method = "p.S/Hello" };

        await pane.Connections.Single().ImportRequestCommand.ExecuteAsync(null);

        var saved = store.Requests.ShouldHaveSingleItem();
        saved.Name.ShouldBe("hello");
        saved.ConnectionId.ShouldBe("c1");   // re-bound to the target connection
        saved.Id.ShouldNotBe("other");       // fresh id
    }

    [Fact]
    public async Task Importing_a_colliding_name_gets_an_imported_suffix()
    {
        var pane = Pane(out var snippetIO, out var picker, out var store, out _, new WorkspaceModel
        {
            Connections = [new SavedConnection { Id = "c1", Name = "alpha", Address = "h:1" }],
            SavedRequests = [new SavedRequest { Id = "r1", Name = "hello", ConnectionId = "c1", Method = "p.S/Hello" }]
        });
        picker.OpenResult = "/in/req.grpcnreq.json";
        snippetIO.ImportResult = new SavedRequest { Id = "x", Name = "hello", ConnectionId = "source", Method = "p.S/Hello" };

        await pane.Connections.Single().ImportRequestCommand.ExecuteAsync(null);

        store.ForConnection("c1").Select(r => r.Name).ShouldBe(["hello", "hello (imported)"]);
    }

    [Fact]
    public async Task A_bad_import_file_reports_an_error_and_saves_nothing()
    {
        var pane = Pane(out var snippetIO, out var picker, out var store, out var dialogs,
            new WorkspaceModel { Connections = [new SavedConnection { Id = "c1", Name = "alpha", Address = "h:1" }] });
        picker.OpenResult = "/in/bad.json";
        snippetIO.ImportError = new SavedRequestSnippetException("not a saved-request snippet.");

        await pane.Connections.Single().ImportRequestCommand.ExecuteAsync(null);

        dialogs.LastMessageTitle.ShouldBe("Could not import request");
        store.Requests.ShouldBeEmpty();
    }
}
