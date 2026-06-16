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
        var store = new TlsProfileStore(workspace);

        await store.SaveAsync(new TlsProfile { Name = "p1" }, TestContext.Current.CancellationToken);

        store.Profiles.ShouldHaveSingleItem().Name.ShouldBe("p1");
        workspace.SaveCount.ShouldBe(1);
    }

    [Fact]
    public async Task Save_replaces_a_profile_with_the_same_id()
    {
        var profile = new TlsProfile { Name = "orig" };
        var workspace = new FakeWorkspaceStore(new WorkspaceModel { TlsProfiles = [profile] });
        var store = new TlsProfileStore(workspace);

        await store.SaveAsync(new TlsProfile { Id = profile.Id, Name = "renamed" }, TestContext.Current.CancellationToken);

        store.Profiles.ShouldHaveSingleItem().Name.ShouldBe("renamed");
    }

    [Fact]
    public async Task Save_preserves_the_connection_list()
    {
        var connection = new SavedConnection { Name = "conn", Address = "a:443" };
        var workspace = new FakeWorkspaceStore(new WorkspaceModel { Connections = [connection] });
        var store = new TlsProfileStore(workspace);

        await store.SaveAsync(new TlsProfile { Name = "p" }, TestContext.Current.CancellationToken);

        workspace.Current.Connections.ShouldHaveSingleItem().Name.ShouldBe("conn");
        workspace.Current.TlsProfiles.ShouldHaveSingleItem();
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
        var store = new TlsProfileStore(workspace);

        store.UsageCount(profile.Id).ShouldBe(2);
        store.UsageCount("missing").ShouldBe(0);

        await Task.CompletedTask;
    }
}
