using GrpCurl.Net.Studio.Services;
using GrpCurl.Net.Studio.TestSupport;
using GrpCurl.Net.Studio.ViewModels;
using GrpCurl.Net.Studio.ViewModels.Connections;
using GrpCurl.Net.Studio.ViewModels.Models.Connections;
using GrpCurl.Net.Studio.ViewModels.Services;

namespace GrpCurl.Net.Studio.Tests.Unit;

/// <summary>
///     L1 tests for <see cref="EnvironmentSwitcherViewModel" /> (FR-133/138): the dropdown lists
///     "No environment" plus the workspace environments, selection drives the active environment, external
///     changes sync the selection, and Reload survives the active environment being deleted.
/// </summary>
public sealed class EnvironmentSwitcherViewModelTests
{
    private static EnvironmentSwitcherViewModel Create(
        out EnvironmentService service,
        out IEnvironmentStore store,
        out FakeDialogService dialog,
        out FakeWorkspaceStore workspace,
        params WorkspaceEnvironment[] environments)
    {
        workspace = new FakeWorkspaceStore(new WorkspaceModel { Environments = environments.ToList() });
        var secrets = new FakeSecretStore();
        service = new EnvironmentService(workspace, secrets);
        store = new EnvironmentStore(workspace, secrets);
        dialog = new FakeDialogService();
        return new EnvironmentSwitcherViewModel(service, store, dialog, secrets);
    }

    private static WorkspaceEnvironment Env(string id, string name) => new() { Id = id, Name = name };

    [Fact]
    public void The_dropdown_leads_with_no_environment_then_each_environment()
    {
        var vm = Create(out _, out _, out _, out _, Env("e1", "staging"), Env("e2", "prod"));

        vm.Options.Select(o => o.Name).ShouldBe(["No environment", "staging", "prod"]);
        vm.Options[0].Id.ShouldBeNull();
        vm.SelectedOption.ShouldBe(vm.Options[0]); // starts with no environment (FR-138)
        vm.IsNoEnvironment.ShouldBeTrue();
        vm.DisplayText.ShouldBe("No environment");
    }

    [Fact]
    public void Selecting_an_environment_makes_it_active()
    {
        var vm = Create(out var service, out _, out _, out _, Env("e1", "staging"));

        vm.SelectedOption = vm.Options.Single(o => o.Id == "e1");

        service.ActiveId.ShouldBe("e1");
        vm.IsNoEnvironment.ShouldBeFalse();
        vm.DisplayText.ShouldBe("staging");
    }

    [Fact]
    public void Selecting_no_environment_clears_the_active_environment()
    {
        var vm = Create(out var service, out _, out _, out _, Env("e1", "staging"));
        vm.SelectedOption = vm.Options.Single(o => o.Id == "e1");

        vm.SelectedOption = vm.Options[0]; // "No environment"

        service.ActiveId.ShouldBeNull();
        vm.IsNoEnvironment.ShouldBeTrue();
    }

    [Fact]
    public void An_external_active_change_syncs_the_selection()
    {
        var vm = Create(out var service, out _, out _, out _, Env("e1", "staging"));

        service.SetActive("e1"); // e.g. another surface set it

        vm.SelectedOption.ShouldNotBeNull().Id.ShouldBe("e1");
        vm.DisplayText.ShouldBe("staging");
    }

    [Fact]
    public async Task Reload_after_the_active_environment_is_deleted_falls_back_to_no_environment()
    {
        var vm = Create(out var service, out var store, out _, out _, Env("e1", "staging"));
        vm.SelectedOption = vm.Options.Single(o => o.Id == "e1");
        service.ActiveId.ShouldBe("e1");

        await store.DeleteAsync("e1", TestContext.Current.CancellationToken);
        vm.Reload();

        vm.Options.Select(o => o.Name).ShouldBe(["No environment"]);
        vm.SelectedOption.ShouldBe(vm.Options[0]);
        service.ActiveId.ShouldBeNull(); // the dangling active selection was cleared
        vm.IsNoEnvironment.ShouldBeTrue();
    }

    [Fact]
    public async Task Manage_opens_the_manager_and_refreshes_the_dropdown()
    {
        var vm = Create(out _, out var store, out var dialog, out _);
        vm.Options.ShouldHaveSingleItem(); // just "No environment"

        // Simulate the manager adding an environment while the dialog is open.
        dialog.OnShowDialog = d =>
        {
            if (d is EnvironmentManagerViewModel)
            {
                store.SaveAsync(Env("e1", "staging")).GetAwaiter().GetResult();
            }

            return false;
        };

        await vm.ManageCommand.ExecuteAsync(null);

        vm.Options.Select(o => o.Name).ShouldBe(["No environment", "staging"]);
    }
}
