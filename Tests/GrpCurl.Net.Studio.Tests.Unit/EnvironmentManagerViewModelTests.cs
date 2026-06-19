using GrpCurl.Net.Studio.Services;
using GrpCurl.Net.Studio.TestSupport;
using GrpCurl.Net.Studio.ViewModels.Connections;
using GrpCurl.Net.Studio.ViewModels.Models.Connections;
using GrpCurl.Net.Studio.ViewModels.Services;

namespace GrpCurl.Net.Studio.Tests.Unit;

/// <summary>
///     L1 tests for <see cref="EnvironmentManagerViewModel" /> (E3.2 PR-B): row population + summary, the
///     create/edit/duplicate/delete paths (driven through a scripted dialog), and independent secret copies
///     on duplicate.
/// </summary>
public sealed class EnvironmentManagerViewModelTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static EnvironmentManagerViewModel Create(
        out FakeDialogService dialog, out IEnvironmentStore store, out FakeSecretStore secrets, WorkspaceModel? workspace = null)
    {
        dialog = new FakeDialogService();
        secrets = new FakeSecretStore();
        store = new EnvironmentStore(new FakeWorkspaceStore(workspace ?? new WorkspaceModel()), secrets);
        return new EnvironmentManagerViewModel(store, dialog, secrets);
    }

    [Fact]
    public void Rows_summarise_variable_and_secret_counts()
    {
        var workspace = new WorkspaceModel
        {
            Environments =
            [
                new WorkspaceEnvironment
                {
                    Id = "e1", Name = "staging",
                    Variables =
                    [
                        new EnvironmentVariable { Name = "HOST", Value = StringOrSecret.Plain("h") },
                        new EnvironmentVariable { Name = "TOKEN", Value = StringOrSecret.Secret("r") }
                    ]
                }
            ]
        };
        var vm = Create(out _, out _, out _, workspace);

        var row = vm.Environments.ShouldHaveSingleItem();
        row.Display.ShouldBe("staging");
        row.SummaryText.ShouldBe("2 variable(s), 1 secret");
        vm.HasEnvironments.ShouldBeTrue();
    }

    [Fact]
    public async Task New_environment_saves_and_reloads()
    {
        var vm = Create(out var dialog, out var store, out _);
        dialog.OnShowDialog = d => d is EnvironmentEditorViewModel ? new WorkspaceEnvironment { Id = "e1", Name = "fresh" } : null;

        await vm.NewEnvironmentCommand.ExecuteAsync(null);

        store.Environments.ShouldContain(e => e.Name == "fresh");
        vm.Environments.ShouldContain(r => r.Display == "fresh");
    }

    [Fact]
    public async Task Duplicate_clones_with_a_copy_suffix_and_independent_secret()
    {
        var source = new WorkspaceEnvironment
        {
            Id = "e1", Name = "staging",
            Variables = [new EnvironmentVariable { Name = "TOKEN", Value = StringOrSecret.Secret("ref-1") }]
        };
        var vm = Create(out _, out var store, out var secrets, new WorkspaceModel { Environments = [source] });
        await secrets.SetAsync("ref-1", "s3cr3t", Ct);

        await vm.DuplicateEnvironmentCommand.ExecuteAsync(vm.Environments.Single());

        var copy = store.Environments.Single(e => e.Name == "staging (copy)");
        copy.Id.ShouldNotBe(source.Id);
        var copyRef = copy.Variables.Single().Value.SecretRef.ShouldNotBeNull();
        copyRef.ShouldNotBe("ref-1"); // its own secret entry
        (await secrets.GetAsync(copyRef, Ct)).ShouldBe("s3cr3t");
    }

    [Fact]
    public async Task Delete_confirmed_removes_the_environment()
    {
        var vm = Create(out var dialog, out var store, out _,
            new WorkspaceModel { Environments = [new WorkspaceEnvironment { Id = "e1", Name = "doomed" }] });
        dialog.ConfirmResult = true;

        await vm.DeleteEnvironmentCommand.ExecuteAsync(vm.Environments.Single());

        store.Environments.ShouldBeEmpty();
        vm.Environments.ShouldBeEmpty();
    }

    [Fact]
    public async Task Delete_declined_keeps_the_environment()
    {
        var vm = Create(out var dialog, out var store, out _,
            new WorkspaceModel { Environments = [new WorkspaceEnvironment { Id = "e1", Name = "kept" }] });
        dialog.ConfirmResult = false;

        await vm.DeleteEnvironmentCommand.ExecuteAsync(vm.Environments.Single());

        _ = store.Environments.ShouldHaveSingleItem();
    }

    [Fact]
    public void Close_reports_whether_anything_changed()
    {
        var vm = Create(out _, out _, out _);
        bool? result = null;
        vm.CloseRequested += r => result = r;

        vm.CloseCommand.Execute(null);

        result.ShouldBe(false);
    }
}
