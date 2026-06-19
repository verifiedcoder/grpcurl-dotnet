using GrpCurl.Net.Studio.Services;
using GrpCurl.Net.Studio.TestSupport;
using GrpCurl.Net.Studio.ViewModels.Connections;
using GrpCurl.Net.Studio.ViewModels.Models.Connections;
using GrpCurl.Net.Studio.ViewModels.Models.History;
using GrpCurl.Net.Studio.ViewModels.Panes;
using GrpCurl.Net.Studio.ViewModels.Services;

namespace GrpCurl.Net.Studio.Tests.Unit;

public sealed class ConnectionsPaneViewModelTests
{
    private static ConnectionsPaneViewModel Create(
        out FakeWorkspaceStore store,
        out FakeDialogService dialogs,
        WorkspaceModel? initial = null)
    {
        store = new FakeWorkspaceStore(initial);
        dialogs = new FakeDialogService();
        return new ConnectionsPaneViewModel(store, new FakeConnectionRegistry(), dialogs, new ConnectionSelection());
    }

    [Fact]
    public void Loads_existing_connections_from_the_workspace()
    {
        var initial = new WorkspaceModel { Connections = [new SavedConnection { Name = "a", Address = "h:1" }] };
        var pane = Create(out _, out _, initial);

        pane.Connections.Count.ShouldBe(1);
        pane.HasConnections.ShouldBeTrue();
    }

    [Fact]
    public async Task Add_connection_appends_and_persists_when_dialog_returns_a_connection()
    {
        var pane = Create(out var store, out var dialogs);
        dialogs.OnShowDialog = vm =>
        {
            var editor = (ConnectionEditorViewModel)vm;
            editor.Name = "new";
            editor.Address = "localhost:9090";
            return editor.BuildConnection();
        };

        pane.HasConnections.ShouldBeFalse();

        await pane.AddConnectionCommand.ExecuteAsync(null);

        pane.Connections.Single().Name.ShouldBe("new");
        pane.HasConnections.ShouldBeTrue();
        store.SaveCount.ShouldBe(1);
        store.Current.Connections.Single().Address.ShouldBe("localhost:9090");
    }

    [Fact]
    public async Task Add_connection_cancelled_dialog_changes_nothing()
    {
        var pane = Create(out var store, out var dialogs);
        dialogs.OnShowDialog = _ => null; // user cancelled

        await pane.AddConnectionCommand.ExecuteAsync(null);

        pane.Connections.ShouldBeEmpty();
        store.SaveCount.ShouldBe(0);
    }

    [Fact]
    public async Task Edit_replaces_the_item_in_place()
    {
        var initial = new WorkspaceModel { Connections = [new SavedConnection { Name = "old", Address = "h:1" }] };
        var pane = Create(out _, out var dialogs, initial);
        dialogs.OnShowDialog = vm =>
        {
            var editor = (ConnectionEditorViewModel)vm;
            editor.Name = "renamed";
            return editor.BuildConnection();
        };

        await pane.EditConnectionCommand.ExecuteAsync(pane.Connections[0]);

        pane.Connections.Single().Name.ShouldBe("renamed");
    }

    [Fact]
    public async Task Duplicate_appends_a_copy_with_a_new_id()
    {
        var original = new SavedConnection { Name = "prod", Address = "h:1" };
        var pane = Create(out var store, out _, new WorkspaceModel { Connections = [original] });

        await pane.DuplicateConnectionCommand.ExecuteAsync(pane.Connections[0]);

        pane.Connections.Count.ShouldBe(2);
        pane.Connections[1].Name.ShouldBe("prod (copy)");
        pane.Connections[1].Id.ShouldNotBe(original.Id);
        store.SaveCount.ShouldBe(1);
    }

    [Fact]
    public void Add_is_always_enabled()
    {
        var pane = Create(out _, out _);

        pane.AddConnectionCommand.CanExecute(null).ShouldBeTrue();
    }

    [Fact]
    public void Edit_duplicate_delete_are_enabled_only_when_a_connection_is_selected()
    {
        var pane = Create(out _, out _,
            new WorkspaceModel { Connections = [new SavedConnection { Name = "a", Address = "h:1" }] });

        // No selection: the selection-scoped commands are disabled (covers the empty case too).
        pane.SelectedConnection.ShouldBeNull();
        pane.EditConnectionCommand.CanExecute(null).ShouldBeFalse();
        pane.DuplicateConnectionCommand.CanExecute(null).ShouldBeFalse();
        pane.DeleteConnectionCommand.CanExecute(null).ShouldBeFalse();

        // Selecting enables them...
        pane.SelectedConnection = pane.Connections[0];
        pane.EditConnectionCommand.CanExecute(null).ShouldBeTrue();
        pane.DuplicateConnectionCommand.CanExecute(null).ShouldBeTrue();
        pane.DeleteConnectionCommand.CanExecute(null).ShouldBeTrue();

        // ...and clearing the selection disables them again.
        pane.SelectedConnection = null;
        pane.EditConnectionCommand.CanExecute(null).ShouldBeFalse();
        pane.DuplicateConnectionCommand.CanExecute(null).ShouldBeFalse();
        pane.DeleteConnectionCommand.CanExecute(null).ShouldBeFalse();
    }

    [Fact]
    public async Task Delete_removes_only_when_confirmed()
    {
        var pane = Create(out var store, out var dialogs,
            new WorkspaceModel { Connections = [new SavedConnection { Name = "a", Address = "h:1" }] });

        dialogs.ConfirmResult = false;
        await pane.DeleteConnectionCommand.ExecuteAsync(pane.Connections[0]);
        pane.Connections.Count.ShouldBe(1);

        dialogs.ConfirmResult = true;
        await pane.DeleteConnectionCommand.ExecuteAsync(pane.Connections[0]);
        pane.Connections.ShouldBeEmpty();
        pane.HasConnections.ShouldBeFalse();
        store.SaveCount.ShouldBe(1);
    }

    // ── FR-126: history purge on connection delete ───────────────────────────

    private static HistoryEntry Hist(string id, string connectionName, string address) => new(
        HistoryEntry.CurrentVersion, id, new DateTimeOffset(2026, 6, 11, 12, 0, 0, TimeSpan.Zero), HistoryKind.Grpc,
        new HistoryConnection(connectionName, address, "plaintext", null), null, "pkg.Svc/M",
        new HistoryRequest("json", "{}", false, [], null, false, true, null, null, null),
        new HistoryOutcome("OK", "success", 0, 1, 1, 1, null, false, null));

    private static ConnectionsPaneViewModel CreateWithHistory(out FakeDialogService dialogs, out FakeHistoryStore history)
    {
        var store = new FakeWorkspaceStore(new WorkspaceModel { Connections = [new SavedConnection { Name = "a", Address = "h:1" }] });
        dialogs = new FakeDialogService();
        history = new FakeHistoryStore();
        history.Entries.AddRange([Hist("e1", "a", "h:1"), Hist("e2", "a", "h:1"), Hist("e3", "other", "h:9")]);
        return new ConnectionsPaneViewModel(store, new FakeConnectionRegistry(), dialogs, new ConnectionSelection(), history: history);
    }

    [Fact]
    public async Task Delete_purges_matching_history_when_the_box_is_ticked()
    {
        var pane = CreateWithHistory(out var dialogs, out var history);
        dialogs.OnShowDialog = d => d is DeleteConnectionDialogViewModel ? (bool?)true : null;

        await pane.DeleteConnectionCommand.ExecuteAsync(pane.Connections[0]);

        pane.Connections.ShouldBeEmpty();
        history.Entries.Select(e => e.Id).ShouldBe(["e3"]); // only the other connection's entry survives
    }

    [Fact]
    public async Task Delete_keeps_history_when_the_box_is_unticked()
    {
        var pane = CreateWithHistory(out var dialogs, out var history);
        dialogs.OnShowDialog = d => d is DeleteConnectionDialogViewModel ? (bool?)false : null;

        await pane.DeleteConnectionCommand.ExecuteAsync(pane.Connections[0]);

        pane.Connections.ShouldBeEmpty();
        history.Entries.Count.ShouldBe(3); // connection gone, history untouched
    }

    [Fact]
    public async Task Cancelling_the_delete_dialog_keeps_the_connection_and_history()
    {
        var pane = CreateWithHistory(out var dialogs, out var history);
        dialogs.OnShowDialog = _ => null; // cancelled

        await pane.DeleteConnectionCommand.ExecuteAsync(pane.Connections[0]);

        pane.Connections.Count.ShouldBe(1);
        history.Entries.Count.ShouldBe(3);
    }

    // ── FR-145: saved requests nested under their connection ─────────────────

    private static ConnectionsPaneViewModel CreateWithSavedRequests(
        out FakeDocumentHost host, WorkspaceModel initial)
    {
        var store = new FakeWorkspaceStore(initial);
        host = new FakeDocumentHost();
        return new ConnectionsPaneViewModel(
            store, new FakeConnectionRegistry(), new FakeDialogService(), new ConnectionSelection(),
            savedRequests: new SavedRequestStore(store), documentHost: host);
    }

    [Fact]
    public void New_graphql_operation_opens_a_graphql_tab_for_the_connection()
    {
        var workspace = new WorkspaceModel { Connections = [new SavedConnection { Id = "c1", Name = "alpha", Address = "h:1" }] };
        var pane = CreateWithSavedRequests(out var host, workspace);

        pane.NewGraphQlOperationCommand.Execute(pane.Connections[0]);

        _ = host.LastGraphQl.ShouldNotBeNull();
        host.LastGraphQl!.Id.ShouldBe("c1");
    }

    [Fact]
    public void Saved_requests_are_grouped_under_their_connection()
    {
        var workspace = new WorkspaceModel
        {
            Connections =
            [
                new SavedConnection { Id = "c1", Name = "alpha", Address = "h:1" },
                new SavedConnection { Id = "c2", Name = "beta", Address = "h:2" }
            ],
            SavedRequests =
            [
                new SavedRequest { Id = "r1", Name = "hello", ConnectionId = "c1", Method = "p.S/Hello" },
                new SavedRequest { Id = "r2", Name = "bye", ConnectionId = "c1", Method = "p.S/Bye" },
                new SavedRequest { Id = "r3", Name = "ping", ConnectionId = "c2", Method = "p.S/Ping" }
            ]
        };
        var pane = CreateWithSavedRequests(out _, workspace);

        var alpha = pane.Connections.Single(c => c.Id == "c1");
        alpha.HasSavedRequests.ShouldBeTrue();
        alpha.SavedRequests.Select(r => r.Name).ShouldBe(["hello", "bye"]);
        pane.Connections.Single(c => c.Id == "c2").SavedRequests.Single().Name.ShouldBe("ping");
    }

    [Fact]
    public void A_connection_without_saved_requests_shows_none()
    {
        var pane = CreateWithSavedRequests(out _,
            new WorkspaceModel { Connections = [new SavedConnection { Id = "c1", Name = "a", Address = "h:1" }] });

        pane.Connections.Single().HasSavedRequests.ShouldBeFalse();
    }

    [Fact]
    public async Task Opening_a_saved_request_routes_to_the_document_host()
    {
        var workspace = new WorkspaceModel
        {
            Connections = [new SavedConnection { Id = "c1", Name = "alpha", Address = "h:1" }],
            SavedRequests = [new SavedRequest { Id = "r1", Name = "hello", ConnectionId = "c1", Method = "p.S/Hello" }]
        };
        var pane = CreateWithSavedRequests(out var host, workspace);

        await pane.Connections.Single().SavedRequests.Single().OpenCommand.ExecuteAsync(null);

        var opened = host.LastSavedRequest.ShouldNotBeNull();
        opened.Connection.Id.ShouldBe("c1");
        opened.Request.Name.ShouldBe("hello");
    }
}
