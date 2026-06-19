using GrpCurl.Net.Studio.Services;
using GrpCurl.Net.Studio.TestSupport;
using GrpCurl.Net.Studio.ViewModels.Connections;
using GrpCurl.Net.Studio.ViewModels.Models.Connections;
using GrpCurl.Net.Studio.ViewModels.Services;

namespace GrpCurl.Net.Studio.Tests.Unit;

/// <summary>
///     L1 tests for <see cref="TlsProfileManagerViewModel" />: row population with usage counts,
///     duplicate, and delete (with confirmation + reference reversion). The editor-open paths are driven
///     through a scripted <see cref="FakeDialogService" />.
/// </summary>
public sealed class TlsProfileManagerViewModelTests
{
    private static TlsProfileManagerViewModel Create(
        out FakeDialogService dialog, out ITlsProfileStore store, out FakeSecretStore secrets, WorkspaceModel? workspace = null)
    {
        dialog = new FakeDialogService();
        secrets = new FakeSecretStore();
        store = new TlsProfileStore(new FakeWorkspaceStore(workspace ?? new WorkspaceModel()), secrets);
        return new TlsProfileManagerViewModel(store, new FakeFilePickerService(), dialog, secrets);
    }

    [Fact]
    public void Rows_show_each_profile_with_its_usage_count()
    {
        var profile = new TlsProfile { Name = "mtls" };
        var workspace = new WorkspaceModel
        {
            TlsProfiles = [profile],
            Connections = [new SavedConnection { Name = "a", TlsProfileId = profile.Id }]
        };
        var vm = Create(out _, out _, out _, workspace);

        var row = vm.Profiles.ShouldHaveSingleItem();
        row.Display.ShouldBe("mtls");
        row.UsageText.ShouldBe("used by 1 connection");
        vm.HasProfiles.ShouldBeTrue();
    }

    [Fact]
    public async Task New_profile_saves_and_reloads()
    {
        var vm = Create(out var dialog, out var store, out _);
        dialog.OnShowDialog = d => d is TlsProfileEditorViewModel ? new TlsProfile { Name = "fresh" } : null;

        await vm.NewProfileCommand.ExecuteAsync(null);

        store.Profiles.ShouldContain(p => p.Name == "fresh");
        vm.Profiles.ShouldContain(r => r.Display == "fresh");
    }

    [Fact]
    public async Task Duplicate_clones_with_a_copy_suffix_and_independent_secret()
    {
        var source = new TlsProfile { Name = "mtls", ClientCertPath = "/c.pfx", ClientCertPasswordSecretRef = "ref-1" };
        var vm = Create(out _, out var store, out var secrets, new WorkspaceModel { TlsProfiles = [source] });
        await secrets.SetAsync("ref-1", "pw", TestContext.Current.CancellationToken);

        var row = vm.Profiles.Single();
        await vm.DuplicateProfileCommand.ExecuteAsync(row);

        var copy = store.Profiles.Single(p => p.Name == "mtls (copy)");
        copy.Id.ShouldNotBe(source.Id);
        _ = copy.ClientCertPasswordSecretRef.ShouldNotBeNull();
        copy.ClientCertPasswordSecretRef.ShouldNotBe("ref-1"); // its own secret entry
        (await secrets.GetAsync(copy.ClientCertPasswordSecretRef!, TestContext.Current.CancellationToken)).ShouldBe("pw");
    }

    [Fact]
    public async Task Delete_confirmed_removes_the_profile()
    {
        var profile = new TlsProfile { Name = "doomed" };
        var vm = Create(out var dialog, out var store, out _, new WorkspaceModel { TlsProfiles = [profile] });
        dialog.ConfirmResult = true;

        await vm.DeleteProfileCommand.ExecuteAsync(vm.Profiles.Single());

        store.Profiles.ShouldBeEmpty();
        vm.Profiles.ShouldBeEmpty();
    }

    [Fact]
    public async Task Delete_declined_keeps_the_profile()
    {
        var profile = new TlsProfile { Name = "kept" };
        var vm = Create(out var dialog, out var store, out _, new WorkspaceModel { TlsProfiles = [profile] });
        dialog.ConfirmResult = false;

        await vm.DeleteProfileCommand.ExecuteAsync(vm.Profiles.Single());

        _ = store.Profiles.ShouldHaveSingleItem();
    }

    [Fact]
    public void Close_reports_whether_anything_changed()
    {
        var vm = Create(out _, out _, out _);
        bool? result = null;
        vm.CloseRequested += r => result = r;

        vm.CloseCommand.Execute(null);

        result.ShouldBe(false); // nothing changed
    }

    // ── FR-038: usage click-through lists the referencing connections ─────────

    [Fact]
    public async Task Showing_usage_lists_the_referencing_connections()
    {
        var profile = new TlsProfile { Name = "mtls" };
        var workspace = new WorkspaceModel
        {
            TlsProfiles = [profile],
            Connections =
            [
                new SavedConnection { Name = "alpha", TlsProfileId = profile.Id },
                new SavedConnection { Name = "beta", TlsProfileId = profile.Id }
            ]
        };
        var vm = Create(out var dialog, out _, out _, workspace);
        var row = vm.Profiles.Single();
        row.HasUsage.ShouldBeTrue();

        await vm.ShowUsageCommand.ExecuteAsync(row);

        dialog.MessageCount.ShouldBe(1);
        var body = dialog.LastMessageBody.ShouldNotBeNull();
        body.ShouldContain("alpha");
        body.ShouldContain("beta");
    }

    [Fact]
    public async Task Showing_usage_for_an_unused_profile_is_a_no_op()
    {
        var profile = new TlsProfile { Name = "spare" };
        var vm = Create(out var dialog, out _, out _, new WorkspaceModel { TlsProfiles = [profile] });
        var row = vm.Profiles.Single();
        row.HasUsage.ShouldBeFalse();

        await vm.ShowUsageCommand.ExecuteAsync(row);

        dialog.MessageCount.ShouldBe(0);
    }
}
