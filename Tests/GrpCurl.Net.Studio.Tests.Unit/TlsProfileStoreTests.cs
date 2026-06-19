using GrpCurl.Net.Studio.Services;
using GrpCurl.Net.Studio.TestSupport;
using GrpCurl.Net.Studio.ViewModels.Models.Connections;

namespace GrpCurl.Net.Studio.Tests.Unit;

/// <summary>
///     L1 tests for <see cref="TlsProfileStore" />: upsert semantics and the invariant that a profile
///     save preserves the connection list (the workspace is shared with the connections pane).
/// </summary>
public sealed class TlsProfileStoreTests
{
    [Fact]
    public async Task Save_adds_a_new_profile()
    {
        var workspace = new FakeWorkspaceStore();
        var store = new TlsProfileStore(workspace, new FakeSecretStore());

        await store.SaveAsync(new TlsProfile { Name = "p1" }, TestContext.Current.CancellationToken);

        store.Profiles.ShouldHaveSingleItem().Name.ShouldBe("p1");
        workspace.SaveCount.ShouldBe(1);
    }

    [Fact]
    public async Task Save_replaces_a_profile_with_the_same_id()
    {
        var profile = new TlsProfile { Name = "orig" };
        var workspace = new FakeWorkspaceStore(new WorkspaceModel { TlsProfiles = [profile] });
        var store = new TlsProfileStore(workspace, new FakeSecretStore());

        await store.SaveAsync(new TlsProfile { Id = profile.Id, Name = "renamed" }, TestContext.Current.CancellationToken);

        store.Profiles.ShouldHaveSingleItem().Name.ShouldBe("renamed");
    }

    [Fact]
    public async Task Save_preserves_the_connection_list()
    {
        var connection = new SavedConnection { Name = "conn", Address = "a:443" };
        var workspace = new FakeWorkspaceStore(new WorkspaceModel { Connections = [connection] });
        var store = new TlsProfileStore(workspace, new FakeSecretStore());

        await store.SaveAsync(new TlsProfile { Name = "p" }, TestContext.Current.CancellationToken);

        workspace.Current.Connections.ShouldHaveSingleItem().Name.ShouldBe("conn");
        _ = workspace.Current.TlsProfiles.ShouldHaveSingleItem();
    }

    [Fact]
    public async Task Delete_removes_the_profile_and_reverts_referencing_connections()
    {
        var profile = new TlsProfile { Name = "p" };
        var connection = new SavedConnection { Name = "c", TlsProfileId = profile.Id };
        var workspace = new FakeWorkspaceStore(new WorkspaceModel { TlsProfiles = [profile], Connections = [connection] });
        var store = new TlsProfileStore(workspace, new FakeSecretStore());

        await store.DeleteAsync(profile.Id, TestContext.Current.CancellationToken);

        store.Profiles.ShouldBeEmpty();
        workspace.Current.Connections.ShouldHaveSingleItem().TlsProfileId.ShouldBeNull();
    }

    [Fact]
    public async Task Delete_purges_the_pkcs12_password_secret()
    {
        var profile = new TlsProfile { Name = "pfx", ClientCertPasswordSecretRef = "secret-1" };
        var workspace = new FakeWorkspaceStore(new WorkspaceModel { TlsProfiles = [profile] });
        var secrets = new FakeSecretStore();
        await secrets.SetAsync("secret-1", "pw", TestContext.Current.CancellationToken);
        var store = new TlsProfileStore(workspace, secrets);

        await store.DeleteAsync(profile.Id, TestContext.Current.CancellationToken);

        (await secrets.GetAsync("secret-1", TestContext.Current.CancellationToken)).ShouldBeNull();
    }

    [Fact]
    public async Task Delete_of_an_unknown_id_is_a_no_op()
    {
        var profile = new TlsProfile { Name = "p" };
        var workspace = new FakeWorkspaceStore(new WorkspaceModel { TlsProfiles = [profile] });
        var store = new TlsProfileStore(workspace, new FakeSecretStore());

        await store.DeleteAsync("missing", TestContext.Current.CancellationToken);

        _ = store.Profiles.ShouldHaveSingleItem();
    }

    [Fact]
    public void Referencing_connections_lists_names()
    {
        var profile = new TlsProfile { Name = "p" };
        var workspace = new FakeWorkspaceStore(new WorkspaceModel
        {
            TlsProfiles = [profile],
            Connections = [new SavedConnection { Name = "alpha", TlsProfileId = profile.Id }]
        });
        var store = new TlsProfileStore(workspace, new FakeSecretStore());

        store.ReferencingConnections(profile.Id).ShouldBe(["alpha"]);
    }

    [Fact]
    public async Task Usage_count_reflects_referencing_connections()
    {
        var profile = new TlsProfile { Name = "p" };
        var workspace = new FakeWorkspaceStore(new WorkspaceModel
        {
            TlsProfiles = [profile],
            Connections =
            [
                new SavedConnection { Name = "a", TlsProfileId = profile.Id },
                new SavedConnection { Name = "b", TlsProfileId = profile.Id },
                new SavedConnection { Name = "c", TlsProfileId = "other" }
            ]
        });
        var store = new TlsProfileStore(workspace, new FakeSecretStore());

        store.UsageCount(profile.Id).ShouldBe(2);
        store.UsageCount("missing").ShouldBe(0);

        await Task.CompletedTask;
    }
}
