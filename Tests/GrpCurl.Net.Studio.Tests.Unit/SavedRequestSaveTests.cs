using GrpCurl.Net.Studio.Services;
using GrpCurl.Net.Studio.TestSupport;
using GrpCurl.Net.Studio.ViewModels.Connections;
using GrpCurl.Net.Studio.ViewModels.Documents;
using GrpCurl.Net.Studio.ViewModels.Models.Connections;
using GrpCurl.Net.Studio.ViewModels.Panes;
using GrpCurl.Net.Studio.ViewModels.Services;

namespace GrpCurl.Net.Studio.Tests.Unit;

/// <summary>
///     FR-145 PR-C: saving a tab as a named request (FR-078), tracking divergence from the saved copy
///     (FR-002), and the sidebar refreshing as requests are saved.
/// </summary>
public sealed class SavedRequestSaveTests
{
    private static SavedConnection Conn() => new() { Id = "c1", Name = "c", Address = "h:1" };

    private static InvocationDocumentViewModel Tab(
        out SavedRequestStore store, out FakeWorkspaceStore workspace, out FakeDialogService dialogs, string? body = "{}")
    {
        workspace = new FakeWorkspaceStore(new WorkspaceModel());
        store = new SavedRequestStore(workspace);
        dialogs = new FakeDialogService();
        return new InvocationDocumentViewModel(
            Conn(), "pkg.Svc/Go", body, new FakeInvocationRunner(), new FakeDescriptorService(),
            new ImmediateUiDispatcher(), new FakeClipboardService(), dialogs, new FakeLauncherService(),
            new FakeRequestValidator(), savedRequests: store);
    }

    // ── FR-078: save a draft as a named request ──────────────────────────────

    [Fact]
    public async Task Saving_an_unbound_tab_prompts_for_a_name_and_creates_the_request()
    {
        var tab = Tab(out var store, out _, out var dialogs);
        dialogs.OnShowDialog = d => d is TextInputDialogViewModel ? "say hello" : null;
        tab.EmitDefaults = true;

        await tab.SaveRequestCommand.ExecuteAsync(null);

        var saved = store.Requests.ShouldHaveSingleItem();
        saved.Name.ShouldBe("say hello");
        saved.ConnectionId.ShouldBe("c1");
        saved.Method.ShouldBe("pkg.Svc/Go");
        saved.EmitDefaults.ShouldBeTrue();
        tab.Title.ShouldBe("say hello");          // FR-078: title takes the name
        tab.IsSavedRequestDirty.ShouldBeFalse();  // dirty cleared
    }

    [Fact]
    public async Task Cancelling_the_name_prompt_saves_nothing()
    {
        var tab = Tab(out var store, out _, out var dialogs);
        dialogs.OnShowDialog = _ => null; // cancelled

        await tab.SaveRequestCommand.ExecuteAsync(null);

        store.Requests.ShouldBeEmpty();
    }

    [Fact]
    public async Task Saving_a_bound_tab_updates_without_prompting()
    {
        var tab = Tab(out var store, out _, out var dialogs);
        await store.SaveAsync(
            new SavedRequest { Id = "r1", Name = "original", ConnectionId = "c1", Method = "pkg.Svc/Go" },
            TestContext.Current.CancellationToken);
        tab.BindSavedRequest("r1", "original", "{}");
        tab.AllowUnknownFields = false;

        await tab.SaveRequestCommand.ExecuteAsync(null);

        dialogs.OnShowDialog.ShouldBeNull();         // never set → no prompt was needed
        var saved = store.Requests.Single(r => r.Id == "r1");
        saved.Name.ShouldBe("original");
        saved.AllowUnknownFields.ShouldBeFalse();
        tab.IsSavedRequestDirty.ShouldBeFalse();
    }

    // ── FR-002: divergence from the saved copy ───────────────────────────────

    [Fact]
    public void An_unbound_draft_is_never_dirty()
    {
        var tab = Tab(out _, out _, out _);

        tab.EmitDefaults = !tab.EmitDefaults;

        tab.IsSavedRequestDirty.ShouldBeFalse(); // no saved copy to diverge from
    }

    [Fact]
    public void Editing_a_bound_tab_makes_it_dirty_and_marks_the_title()
    {
        var tab = Tab(out _, out _, out _);
        tab.BindSavedRequest("r1", "req", "{}");
        tab.IsSavedRequestDirty.ShouldBeFalse();
        tab.DisplayTitle.ShouldNotContain("●");

        tab.Deadline = "10s";

        tab.IsSavedRequestDirty.ShouldBeTrue();
        tab.DisplayTitle.ShouldContain("●"); // FR-002 dirty marker
    }

    [Fact]
    public void Reverting_an_edit_clears_the_dirty_state()
    {
        var tab = Tab(out _, out _, out _);
        tab.BindSavedRequest("r1", "req", "{}");

        tab.MaxMessageSize = "4096";
        tab.IsSavedRequestDirty.ShouldBeTrue();

        tab.MaxMessageSize = string.Empty; // back to the baseline
        tab.IsSavedRequestDirty.ShouldBeFalse();
    }

    [Fact]
    public void A_header_edit_makes_a_bound_tab_dirty()
    {
        var tab = Tab(out _, out _, out _);
        tab.BindSavedRequest("r1", "req", "{}");

        tab.Headers.Add(new HeaderRowViewModel { Name = "authorization", Value = "x" });

        tab.IsSavedRequestDirty.ShouldBeTrue();
    }

    // ── sidebar refresh on save (FR-145) ─────────────────────────────────────

    [Fact]
    public async Task Saving_a_request_refreshes_the_sidebar()
    {
        var workspace = new FakeWorkspaceStore(new WorkspaceModel
        {
            Connections = [new SavedConnection { Id = "c1", Name = "alpha", Address = "h:1" }]
        });
        var store = new SavedRequestStore(workspace);
        var pane = new ConnectionsPaneViewModel(
            workspace, new FakeConnectionRegistry(), new FakeDialogService(), new ConnectionSelection(),
            savedRequests: store, documentHost: new FakeDocumentHost());
        pane.Connections.Single().HasSavedRequests.ShouldBeFalse();

        await store.SaveAsync(
            new SavedRequest { Id = "r1", Name = "hello", ConnectionId = "c1", Method = "pkg.Svc/Go" },
            TestContext.Current.CancellationToken);

        pane.Connections.Single().SavedRequests.Single().Name.ShouldBe("hello");
    }

    // ── sidebar manage: rename / delete / duplicate (FR-145) ─────────────────

    private static SavedRequestItemViewModel ManagedItem(
        out SavedRequestStore store, out FakeDialogService dialogs, out FakeWorkspaceStore workspace)
    {
        workspace = new FakeWorkspaceStore(new WorkspaceModel
        {
            SavedRequests = [new SavedRequest { Id = "r1", Name = "hello", ConnectionId = "c1", Method = "pkg.Svc/Go" }]
        });
        store = new SavedRequestStore(workspace);
        dialogs = new FakeDialogService();
        var request = store.Requests.Single();
        return new SavedRequestItemViewModel(request, _ => Task.CompletedTask, store, dialogs);
    }

    [Fact]
    public async Task Rename_updates_the_request_name_in_place()
    {
        var item = ManagedItem(out var store, out var dialogs, out _);
        dialogs.OnShowDialog = d => d is TextInputDialogViewModel ? "renamed" : null;

        await item.RenameCommand.ExecuteAsync(null);

        var request = store.Requests.ShouldHaveSingleItem();
        request.Id.ShouldBe("r1");        // same id (rename in place)
        request.Name.ShouldBe("renamed");
    }

    [Fact]
    public async Task Rename_cancelled_changes_nothing()
    {
        var item = ManagedItem(out var store, out var dialogs, out _);
        dialogs.OnShowDialog = _ => null;

        await item.RenameCommand.ExecuteAsync(null);

        store.Requests.Single().Name.ShouldBe("hello");
    }

    [Fact]
    public async Task Duplicate_adds_a_copy_with_a_new_id()
    {
        var item = ManagedItem(out var store, out _, out _);

        await item.DuplicateCommand.ExecuteAsync(null);

        store.Requests.Count.ShouldBe(2);
        var copy = store.Requests.Single(r => r.Name == "hello (copy)");
        copy.Id.ShouldNotBe("r1");
        copy.ConnectionId.ShouldBe("c1"); // stays under the same connection
    }

    [Fact]
    public async Task Delete_confirmed_removes_the_request()
    {
        var item = ManagedItem(out var store, out var dialogs, out _);
        dialogs.ConfirmResult = true;

        await item.DeleteCommand.ExecuteAsync(null);

        store.Requests.ShouldBeEmpty();
    }

    [Fact]
    public async Task Delete_declined_keeps_the_request()
    {
        var item = ManagedItem(out var store, out var dialogs, out _);
        dialogs.ConfirmResult = false;

        await item.DeleteCommand.ExecuteAsync(null);

        store.Requests.ShouldHaveSingleItem();
    }

    [Fact]
    public void Manage_actions_are_disabled_without_a_store()
    {
        var item = new SavedRequestItemViewModel(
            new SavedRequest { Id = "r1", Name = "x", ConnectionId = "c1", Method = "m" }, _ => Task.CompletedTask);

        item.CanManage.ShouldBeFalse();
        item.RenameCommand.CanExecute(null).ShouldBeFalse();
        item.DeleteCommand.CanExecute(null).ShouldBeFalse();
        item.DuplicateCommand.CanExecute(null).ShouldBeFalse();
    }
}
