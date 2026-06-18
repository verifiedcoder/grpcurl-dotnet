using GrpCurl.Net.Studio.TestSupport;
using GrpCurl.Net.Studio.ViewModels;
using GrpCurl.Net.Studio.ViewModels.Models.Connections;
using GrpCurl.Net.Studio.ViewModels.Services;

namespace GrpCurl.Net.Studio.Tests.Unit;

/// <summary>E3.1 PR-C: the shell's workspace session — dirty status, explicit Save, Reload-with-confirm.</summary>
public sealed class WorkspaceSessionViewModelTests
{
    private static WorkspaceSessionViewModel Create(out FakeWorkspaceStore store, out FakeDialogService dialogs)
    {
        store = new FakeWorkspaceStore(new WorkspaceModel { Id = "w1", Name = "Demo" });
        dialogs = new FakeDialogService();
        return new WorkspaceSessionViewModel(store, dialogs);
    }

    [Fact]
    public async Task Status_text_shows_the_file_label_with_a_dirty_dot()
    {
        var session = Create(out var store, out _);
        await StoreSaveAs(store, "/tmp/project.gcnws.json");
        session.Refresh();

        session.FileLabel.ShouldBe("project.gcnws.json");
        session.StatusText.ShouldBe("project.gcnws.json");

        store.SetDirty(true);

        session.IsDirty.ShouldBeTrue();
        session.StatusText.ShouldBe("project.gcnws.json ●");
    }

    [Fact]
    public async Task A_read_only_workspace_shows_a_read_only_status(/* FR-148 */)
    {
        var session = Create(out var store, out _);
        await StoreSaveAs(store, "/tmp/locked.gcnws.json");
        session.Refresh();

        store.SetReadOnly(true);

        session.IsReadOnly.ShouldBeTrue();
        session.StatusText.ShouldBe("locked.gcnws.json — read-only");
    }

    [Fact]
    public void An_untitled_workspace_is_labelled_untitled()
    {
        var session = Create(out _, out _);

        session.FileLabel.ShouldBe("Untitled");
        session.ReloadCommand.CanExecute(null).ShouldBeFalse(); // nothing on disk to reload
    }

    [Fact]
    public async Task Save_command_flushes_through_the_store()
    {
        var session = Create(out var store, out _);

        await session.SaveCommand.ExecuteAsync(null);

        store.SaveNowCount.ShouldBe(1);
    }

    [Fact]
    public async Task Reload_confirms_before_discarding_unsaved_changes()
    {
        var session = Create(out var store, out var dialogs);
        await StoreSaveAs(store, "/tmp/p.gcnws.json");
        session.Refresh();
        store.SetDirty(true);
        dialogs.ConfirmResult = false; // user cancels

        await session.ReloadCommand.ExecuteAsync(null);

        dialogs.ConfirmCount.ShouldBe(1);
        store.ReloadCount.ShouldBe(0); // declined → no reload
    }

    [Fact]
    public async Task Reload_when_confirmed_reloads_and_clears_dirty()
    {
        var session = Create(out var store, out var dialogs);
        await StoreSaveAs(store, "/tmp/p.gcnws.json");
        session.Refresh();
        store.SetDirty(true);
        store.ReloadResult = new WorkspaceModel { Id = "w1", Name = "Reloaded" };
        dialogs.ConfirmResult = true;

        await session.ReloadCommand.ExecuteAsync(null);

        store.ReloadCount.ShouldBe(1);
        session.WorkspaceName.ShouldBe("Reloaded");
        session.IsDirty.ShouldBeFalse();
    }

    [Fact]
    public async Task Reload_without_unsaved_changes_skips_the_confirmation()
    {
        var session = Create(out var store, out var dialogs);
        await StoreSaveAs(store, "/tmp/p.gcnws.json");
        session.Refresh();

        await session.ReloadCommand.ExecuteAsync(null);

        dialogs.ConfirmCount.ShouldBe(0); // nothing to lose, no prompt
        store.ReloadCount.ShouldBe(1);
    }

    [Fact]
    public async Task Reload_surfaces_a_schema_error_from_disk()
    {
        var session = Create(out var store, out var dialogs);
        await StoreSaveAs(store, "/tmp/p.gcnws.json");
        session.Refresh();
        store.ReloadError = WorkspaceSchemaException.NewerVersion(2, 1);

        await session.ReloadCommand.ExecuteAsync(null);

        dialogs.MessageCount.ShouldBe(1);
        dialogs.LastMessageTitle.ShouldBe("Could not reload workspace");
    }

    private static Task StoreSaveAs(FakeWorkspaceStore store, string path)
        => store.SaveAsAsync(store.Current, path);
}
