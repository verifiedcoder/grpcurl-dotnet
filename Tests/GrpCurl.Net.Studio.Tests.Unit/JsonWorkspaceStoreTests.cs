using GrpCurl.Net.Studio.Services;
using GrpCurl.Net.Studio.ViewModels.Models.Connections;
using GrpCurl.Net.Studio.ViewModels.Services;

namespace GrpCurl.Net.Studio.Tests.Unit;

public sealed class JsonWorkspaceStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "grpcn-ws-tests-" + Guid.NewGuid().ToString("N"));

    public JsonWorkspaceStoreTests() => Directory.CreateDirectory(_dir);

    private string Path_ => Path.Combine(_dir, "workspace.json");

    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, recursive: true);
        }
    }

    [Fact]
    public async Task Save_then_load_round_trips_connections()
    {
        var ct = TestContext.Current.CancellationToken;
        var store = new JsonWorkspaceStore(Path_);

        var workspace = new WorkspaceModel
        {
            Connections =
            [
                new SavedConnection
                {
                    Name = "staging",
                    Address = "api.example.com:443",
                    Transport = TransportMode.Tls,
                    ConnectTimeout = "10s",
                    Authority = "edge",
                    ReflectionHeaders = [new HeaderEntry { Name = "authorization", Value = "Bearer x" }]
                }
            ]
        };

        await store.SaveAsync(workspace, ct);

        File.Exists(Path_).ShouldBeTrue();

        var reloaded = await new JsonWorkspaceStore(Path_).LoadAsync(ct);

        reloaded.SchemaVersion.ShouldBe(1);
        reloaded.Connections.Count.ShouldBe(1);
        var c = reloaded.Connections[0];
        c.Name.ShouldBe("staging");
        c.Address.ShouldBe("api.example.com:443");
        c.Transport.ShouldBe(TransportMode.Tls);
        c.ConnectTimeout.ShouldBe("10s");
        c.Authority.ShouldBe("edge");
        c.ReflectionHeaders.Single().Name.ShouldBe("authorization");
    }

    [Fact]
    public async Task Enum_serializes_as_string()
    {
        var ct = TestContext.Current.CancellationToken;
        var store = new JsonWorkspaceStore(Path_);

        await store.SaveAsync(new WorkspaceModel { Connections = [new SavedConnection { Name = "p", Address = "h:1", Transport = TransportMode.Plaintext }] }, ct);

        var text = await File.ReadAllTextAsync(Path_, ct);
        text.ShouldContain("plaintext");
        text.ShouldNotContain("\"transport\": 1");
    }

    [Fact]
    public async Task Load_missing_file_returns_empty()
    {
        var settings = await new JsonWorkspaceStore(Path_).LoadAsync(TestContext.Current.CancellationToken);

        settings.Connections.ShouldBeEmpty();
        settings.SchemaVersion.ShouldBe(1);
    }

    // ── E3.1 PR-B: open / save-as / new + recents ────────────────────────────

    private string DocPath(string name) => Path.Combine(_dir, name + ".gcnws.json");

    private static WorkspaceModel Named(string name) => new() { Id = Guid.NewGuid().ToString("D"), Name = name };

    [Fact]
    public async Task Save_as_writes_the_document_sets_current_path_and_heads_recents()
    {
        var ct = TestContext.Current.CancellationToken;
        var store = new JsonWorkspaceStore(Path_);
        var target = DocPath("project-a");

        await store.SaveAsAsync(Named("Project A"), target, ct);

        File.Exists(target).ShouldBeTrue();
        store.CurrentPath.ShouldBe(target);
        store.RecentWorkspaces[0].Path.ShouldBe(Path.GetFullPath(target));
    }

    [Fact]
    public async Task Open_loads_a_document_sets_current_path_and_heads_recents()
    {
        var ct = TestContext.Current.CancellationToken;
        var target = DocPath("project-b");
        await new JsonWorkspaceStore(Path_).SaveAsAsync(Named("Project B"), target, ct);

        var store = new JsonWorkspaceStore(Path_);
        var opened = await store.OpenAsync(target, ct);

        opened.Name.ShouldBe("Project B");
        store.Current.Name.ShouldBe("Project B");
        store.CurrentPath.ShouldBe(target);
        store.RecentWorkspaces[0].Path.ShouldBe(Path.GetFullPath(target));
    }

    [Fact]
    public async Task Open_a_newer_version_throws_and_leaves_current_untouched()
    {
        var ct = TestContext.Current.CancellationToken;
        var target = DocPath("future");
        await File.WriteAllTextAsync(target, """{ "schemaVersion": 999, "id": "x", "name": "future" }""", ct);
        var store = new JsonWorkspaceStore(Path_);
        var before = store.Current;

        var ex = await Should.ThrowAsync<WorkspaceSchemaException>(() => store.OpenAsync(target, ct));

        ex.IsNewerVersion.ShouldBeTrue();
        store.Current.ShouldBeSameAs(before); // unchanged on failure
        store.CurrentPath.ShouldBe(Path_);     // still the default
    }

    [Fact]
    public async Task Open_a_corrupt_file_throws_a_schema_exception()
    {
        var ct = TestContext.Current.CancellationToken;
        var target = DocPath("broken");
        await File.WriteAllTextAsync(target, "{ not json", ct);

        await Should.ThrowAsync<WorkspaceSchemaException>(() => new JsonWorkspaceStore(Path_).OpenAsync(target, ct));
    }

    [Fact]
    public async Task Recents_dedupe_move_to_front_and_cap_at_ten()
    {
        var ct = TestContext.Current.CancellationToken;
        var store = new JsonWorkspaceStore(Path_);

        for (var i = 0; i < 12; i++)
        {
            await store.SaveAsAsync(Named($"w{i}"), DocPath($"w{i}"), ct);
        }

        await store.SaveAsAsync(Named("w3-again"), DocPath("w3"), ct); // re-touch an existing path

        store.RecentWorkspaces.Count.ShouldBe(10);                       // capped
        store.RecentWorkspaces[0].Path.ShouldBe(Path.GetFullPath(DocPath("w3"))); // moved to front, not duplicated
        store.RecentWorkspaces.Count(r => r.Path == Path.GetFullPath(DocPath("w3"))).ShouldBe(1);
    }

    [Fact]
    public async Task Recents_persist_across_store_instances()
    {
        var ct = TestContext.Current.CancellationToken;
        await new JsonWorkspaceStore(Path_).SaveAsAsync(Named("persisted"), DocPath("persisted"), ct);

        var reopened = new JsonWorkspaceStore(Path_);

        reopened.RecentWorkspaces.ShouldContain(r => r.Path == Path.GetFullPath(DocPath("persisted")));
    }

    [Fact]
    public async Task A_deleted_recent_file_is_flagged_dangling()
    {
        var ct = TestContext.Current.CancellationToken;
        var store = new JsonWorkspaceStore(Path_);
        var target = DocPath("gone");
        await store.SaveAsAsync(Named("gone"), target, ct);

        File.Delete(target);

        store.RecentWorkspaces.Single(r => r.Path == Path.GetFullPath(target)).Exists.ShouldBeFalse();
    }

    [Fact]
    public async Task Remove_recent_drops_the_entry()
    {
        var ct = TestContext.Current.CancellationToken;
        var store = new JsonWorkspaceStore(Path_);
        var target = DocPath("temp");
        await store.SaveAsAsync(Named("temp"), target, ct);

        await store.RemoveRecentAsync(target, ct);

        store.RecentWorkspaces.ShouldNotContain(r => r.Path == Path.GetFullPath(target));
    }

    [Fact]
    public void New_workspace_resets_current_and_clears_the_path()
    {
        var store = new JsonWorkspaceStore(Path_);
        var first = store.NewWorkspace();

        first.Connections.ShouldBeEmpty();
        first.Id.ShouldNotBeNullOrWhiteSpace();
        store.CurrentPath.ShouldBeNull(); // untitled until Save As
    }
}
