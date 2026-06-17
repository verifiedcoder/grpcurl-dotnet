using GrpCurl.Net.Studio.Services;
using GrpCurl.Net.Studio.TestSupport;
using GrpCurl.Net.Studio.ViewModels.Models.Connections;

namespace GrpCurl.Net.Studio.Tests.Unit;

/// <summary>
///     L1 tests for <see cref="EnvironmentStore" /> (E3.2 PR-B): upsert preserves the rest of the workspace,
///     and delete purges the secret-typed variables' stored values.
/// </summary>
public sealed class EnvironmentStoreTests
{
    private CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Saving_an_environment_preserves_connections_and_profiles()
    {
        var workspace = new FakeWorkspaceStore(new WorkspaceModel
        {
            Connections = [new SavedConnection { Name = "api" }],
            TlsProfiles = [new TlsProfile { Name = "mtls" }]
        });
        var store = new EnvironmentStore(workspace, new FakeSecretStore());

        await store.SaveAsync(new WorkspaceEnvironment { Id = "e1", Name = "staging" }, Ct);

        workspace.Current.Connections.ShouldHaveSingleItem();
        workspace.Current.TlsProfiles.ShouldHaveSingleItem();
        store.Environments.ShouldContain(e => e.Name == "staging");
    }

    [Fact]
    public async Task Saving_an_existing_id_replaces_rather_than_appends()
    {
        var workspace = new FakeWorkspaceStore(new WorkspaceModel
        {
            Environments = [new WorkspaceEnvironment { Id = "e1", Name = "old" }]
        });
        var store = new EnvironmentStore(workspace, new FakeSecretStore());

        await store.SaveAsync(new WorkspaceEnvironment { Id = "e1", Name = "new" }, Ct);

        var env = store.Environments.ShouldHaveSingleItem();
        env.Name.ShouldBe("new");
    }

    [Fact]
    public async Task Deleting_an_environment_purges_its_secret_values()
    {
        var secrets = new FakeSecretStore();
        await secrets.SetAsync("ref-1", "s3cr3t", Ct);
        var workspace = new FakeWorkspaceStore(new WorkspaceModel
        {
            Environments =
            [
                new WorkspaceEnvironment
                {
                    Id = "e1", Name = "staging",
                    Variables =
                    [
                        new EnvironmentVariable { Name = "HOST", Value = StringOrSecret.Plain("h") },
                        new EnvironmentVariable { Name = "TOKEN", Value = StringOrSecret.Secret("ref-1") }
                    ]
                }
            ]
        });
        var store = new EnvironmentStore(workspace, secrets);

        await store.DeleteAsync("e1", Ct);

        store.Environments.ShouldBeEmpty();
        (await secrets.GetAsync("ref-1", Ct)).ShouldBeNull(); // secret purged
    }

    [Fact]
    public async Task Deleting_an_unknown_environment_is_a_no_op()
    {
        var workspace = new FakeWorkspaceStore(new WorkspaceModel
        {
            Environments = [new WorkspaceEnvironment { Id = "e1", Name = "keep" }]
        });
        var store = new EnvironmentStore(workspace, new FakeSecretStore());

        await store.DeleteAsync("missing", Ct);

        store.Environments.ShouldHaveSingleItem();
    }
}
