using GrpCurl.Net.Studio.TestSupport;
using GrpCurl.Net.Studio.ViewModels.Connections;
using GrpCurl.Net.Studio.ViewModels.Models.Connections;
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
}
