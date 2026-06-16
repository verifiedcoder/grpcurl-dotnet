using GrpCurl.Net.Studio.Services;
using GrpCurl.Net.Studio.TestSupport;
using GrpCurl.Net.Studio.ViewModels.Connections;
using GrpCurl.Net.Studio.ViewModels.Models.Connections;
using GrpCurl.Net.Studio.ViewModels.Services;

namespace GrpCurl.Net.Studio.Tests.Unit;

/// <summary>
///     L1 tests for the connection editor's TLS profile picker (E2.2 PR-C): option population,
///     selection → <see cref="SavedConnection.TlsProfileId" />, plaintext gating, and the create-profile
///     flow that persists through <see cref="ITlsProfileStore" /> and re-selects.
/// </summary>
public sealed class ConnectionEditorProfilePickerTests
{
    private static ConnectionEditorViewModel Create(
        out FakeDialogService dialog, out ITlsProfileStore store, SavedConnection? existing = null, params TlsProfile[] profiles)
    {
        dialog = new FakeDialogService();
        var workspace = new FakeWorkspaceStore(new WorkspaceModel { TlsProfiles = [.. profiles] });
        store = new TlsProfileStore(workspace, new FakeSecretStore());

        return new ConnectionEditorViewModel(
            new FakeConnectionRegistry(), existing, networkDefaults: null,
            store, new FakeFilePickerService(), dialog, new FakeSecretStore());
    }

    [Fact]
    public void Picker_lists_system_default_then_profiles_and_defaults_to_system()
    {
        var vm = Create(out _, out _, existing: null, new TlsProfile { Name = "mtls" });

        vm.TlsProfiles.Count.ShouldBe(2);
        vm.TlsProfiles[0].Profile.ShouldBeNull(); // system default sentinel
        vm.TlsProfiles[1].Profile!.Name.ShouldBe("mtls");
        vm.SelectedTlsProfile.ShouldBe(vm.TlsProfiles[0]);
    }

    [Fact]
    public void Selecting_a_profile_sets_the_connection_reference()
    {
        var profile = new TlsProfile { Name = "mtls" };
        var vm = Create(out _, out _, existing: null, profile);
        vm.Name = "c";
        vm.Address = "localhost:443";

        vm.SelectedTlsProfile = vm.TlsProfiles[1];

        vm.BuildConnection().TlsProfileId.ShouldBe(profile.Id);
    }

    [Fact]
    public void Editing_a_connection_preselects_its_profile()
    {
        var profile = new TlsProfile { Name = "mtls" };
        var existing = new SavedConnection { Name = "c", Address = "a:443", TlsProfileId = profile.Id };

        var vm = Create(out _, out _, existing, profile);

        vm.SelectedTlsProfile!.Profile!.Id.ShouldBe(profile.Id);
    }

    [Fact]
    public void Plaintext_disables_the_picker_and_drops_the_reference()
    {
        var profile = new TlsProfile { Name = "mtls" };
        var vm = Create(out _, out _, existing: null, profile);
        vm.Name = "c";
        vm.Address = "localhost:443";
        vm.SelectedTlsProfile = vm.TlsProfiles[1];

        vm.IsPlaintext = true;

        vm.IsTlsProfileEnabled.ShouldBeFalse();
        vm.BuildConnection().TlsProfileId.ShouldBeNull();
    }

    [Fact]
    public void Edit_profile_is_only_enabled_for_a_real_profile()
    {
        var vm = Create(out _, out _, existing: null, new TlsProfile { Name = "mtls" });

        vm.SelectedTlsProfile = vm.TlsProfiles[0]; // system default
        vm.EditProfileCommand.CanExecute(null).ShouldBeFalse();

        vm.SelectedTlsProfile = vm.TlsProfiles[1];
        vm.EditProfileCommand.CanExecute(null).ShouldBeTrue();
    }

    [Fact]
    public async Task New_profile_flow_persists_and_selects_the_created_profile()
    {
        var vm = Create(out var dialog, out var store);
        var created = new TlsProfile { Name = "fresh" };
        dialog.OnShowDialog = d => d is TlsProfileEditorViewModel ? created : null;

        await vm.NewProfileCommand.ExecuteAsync(null);

        store.Profiles.ShouldContain(p => p.Id == created.Id);
        vm.TlsProfiles.ShouldContain(o => o.Profile != null && o.Profile.Id == created.Id);
        vm.SelectedTlsProfile!.Profile!.Id.ShouldBe(created.Id);
    }

    [Fact]
    public void Without_profile_services_management_is_disabled_but_system_default_still_shows()
    {
        var vm = new ConnectionEditorViewModel(new FakeConnectionRegistry());

        vm.CanManageProfiles.ShouldBeFalse();
        vm.NewProfileCommand.CanExecute(null).ShouldBeFalse();
        vm.TlsProfiles.ShouldHaveSingleItem().Profile.ShouldBeNull();
    }
}
