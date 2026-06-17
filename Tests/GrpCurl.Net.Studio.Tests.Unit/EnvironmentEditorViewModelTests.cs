using GrpCurl.Net.Studio.TestSupport;
using GrpCurl.Net.Studio.ViewModels.Connections;
using GrpCurl.Net.Studio.ViewModels.Models.Connections;

namespace GrpCurl.Net.Studio.Tests.Unit;

/// <summary>
///     L1 tests for <see cref="EnvironmentEditorViewModel" /> (E3.2 PR-B): name + variable validation,
///     add/remove, and the secret lifecycle on save (write new values, leave existing unchanged when blank,
///     purge secrets orphaned by removal or a secret→plain flip).
/// </summary>
public sealed class EnvironmentEditorViewModelTests
{
    private CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public void A_blank_name_blocks_save()
    {
        var vm = new EnvironmentEditorViewModel(new FakeSecretStore());

        vm.NameError.ShouldNotBeNull();
        vm.SaveCommand.CanExecute(null).ShouldBeFalse();
    }

    [Fact]
    public void Duplicate_variable_names_block_save()
    {
        var vm = new EnvironmentEditorViewModel(new FakeSecretStore()) { Name = "staging" };
        vm.AddVariableCommand.Execute(null);
        vm.AddVariableCommand.Execute(null);
        vm.Variables[0].Name = "HOST";
        vm.Variables[1].Name = "HOST";

        vm.VariableError.ShouldNotBeNull();
        vm.SaveCommand.CanExecute(null).ShouldBeFalse();

        vm.Variables[1].Name = "PORT";

        vm.VariableError.ShouldBeNull();
        vm.SaveCommand.CanExecute(null).ShouldBeTrue();
    }

    [Fact]
    public void Add_and_remove_variable_track_the_collection()
    {
        var vm = new EnvironmentEditorViewModel(new FakeSecretStore()) { Name = "staging" };
        vm.HasVariables.ShouldBeFalse();

        vm.AddVariableCommand.Execute(null);
        vm.HasVariables.ShouldBeTrue();

        vm.RemoveVariableCommand.Execute(vm.Variables[0]);
        vm.HasVariables.ShouldBeFalse();
    }

    [Fact]
    public void Saving_a_plain_variable_returns_a_literal_value()
    {
        WorkspaceEnvironment? saved = null;
        var vm = new EnvironmentEditorViewModel(new FakeSecretStore()) { Name = "staging" };
        vm.CloseRequested += r => saved = r;
        vm.AddVariableCommand.Execute(null);
        vm.Variables[0].Name = "HOST";
        vm.Variables[0].Value = "api:443";

        vm.SaveCommand.Execute(null);

        var variable = saved.ShouldNotBeNull().Variables.ShouldHaveSingleItem();
        variable.Name.ShouldBe("HOST");
        variable.IsSecret.ShouldBeFalse();
        variable.Value.Literal.ShouldBe("api:443");
    }

    [Fact]
    public async Task Saving_a_secret_variable_writes_the_value_and_returns_only_a_ref()
    {
        WorkspaceEnvironment? saved = null;
        var secrets = new FakeSecretStore();
        var vm = new EnvironmentEditorViewModel(secrets) { Name = "staging" };
        vm.CloseRequested += r => saved = r;
        vm.AddVariableCommand.Execute(null);
        vm.Variables[0].Name = "TOKEN";
        vm.Variables[0].IsSecret = true;
        vm.Variables[0].Value = "s3cr3t";

        await vm.SaveCommand.ExecuteAsync(null);

        var variable = saved.ShouldNotBeNull().Variables.ShouldHaveSingleItem();
        variable.IsSecret.ShouldBeTrue();
        variable.Value.Literal.ShouldBeNull();
        var keyRef = variable.Value.SecretRef.ShouldNotBeNull();
        (await secrets.GetAsync(keyRef, Ct)).ShouldBe("s3cr3t");
    }

    [Fact]
    public async Task Editing_a_secret_without_re_entering_it_keeps_the_existing_value()
    {
        var secrets = new FakeSecretStore();
        await secrets.SetAsync("ref-1", "kept", Ct);
        var existing = new WorkspaceEnvironment
        {
            Id = "e1", Name = "staging",
            Variables = [new EnvironmentVariable { Name = "TOKEN", Value = StringOrSecret.Secret("ref-1") }]
        };
        WorkspaceEnvironment? saved = null;
        var vm = new EnvironmentEditorViewModel(secrets, existing);
        vm.CloseRequested += r => saved = r;

        // The secret field starts blank (value never read back); save leaves it untouched.
        vm.Variables[0].Value.ShouldBe(string.Empty);
        await vm.SaveCommand.ExecuteAsync(null);

        var variable = saved.ShouldNotBeNull().Variables.ShouldHaveSingleItem();
        variable.Value.SecretRef.ShouldBe("ref-1");
        (await secrets.GetAsync("ref-1", Ct)).ShouldBe("kept");
    }

    [Fact]
    public async Task Flipping_a_secret_to_plain_purges_the_orphaned_secret()
    {
        var secrets = new FakeSecretStore();
        await secrets.SetAsync("ref-1", "old", Ct);
        var existing = new WorkspaceEnvironment
        {
            Id = "e1", Name = "staging",
            Variables = [new EnvironmentVariable { Name = "TOKEN", Value = StringOrSecret.Secret("ref-1") }]
        };
        WorkspaceEnvironment? saved = null;
        var vm = new EnvironmentEditorViewModel(secrets, existing);
        vm.CloseRequested += r => saved = r;
        vm.Variables[0].IsSecret = false;
        vm.Variables[0].Value = "now-plain";

        await vm.SaveCommand.ExecuteAsync(null);

        saved.ShouldNotBeNull().Variables.Single().Value.Literal.ShouldBe("now-plain");
        (await secrets.GetAsync("ref-1", Ct)).ShouldBeNull(); // orphan purged
    }

    [Fact]
    public async Task Removing_a_secret_variable_purges_its_secret_on_save()
    {
        var secrets = new FakeSecretStore();
        await secrets.SetAsync("ref-1", "old", Ct);
        var existing = new WorkspaceEnvironment
        {
            Id = "e1", Name = "staging",
            Variables = [new EnvironmentVariable { Name = "TOKEN", Value = StringOrSecret.Secret("ref-1") }]
        };
        var vm = new EnvironmentEditorViewModel(secrets, existing);
        vm.RemoveVariableCommand.Execute(vm.Variables[0]);

        await vm.SaveCommand.ExecuteAsync(null);

        (await secrets.GetAsync("ref-1", Ct)).ShouldBeNull();
    }

    [Fact]
    public void Cancel_closes_with_null()
    {
        WorkspaceEnvironment? saved = null;
        var closed = false;
        var vm = new EnvironmentEditorViewModel(new FakeSecretStore()) { Name = "staging" };
        vm.CloseRequested += r => { saved = r; closed = true; };

        vm.CancelCommand.Execute(null);

        closed.ShouldBeTrue();
        saved.ShouldBeNull();
    }

    [Fact]
    public void Blank_named_rows_are_skipped_on_save()
    {
        WorkspaceEnvironment? saved = null;
        var vm = new EnvironmentEditorViewModel(new FakeSecretStore()) { Name = "staging" };
        vm.CloseRequested += r => saved = r;
        vm.AddVariableCommand.Execute(null); // left blank

        vm.SaveCommand.Execute(null);

        saved.ShouldNotBeNull().Variables.ShouldBeEmpty();
    }
}
