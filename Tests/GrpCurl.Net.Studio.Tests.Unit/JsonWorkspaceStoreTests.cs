using GrpCurl.Net.Studio.Services;
using GrpCurl.Net.Studio.ViewModels.Models.Connections;

namespace GrpCurl.Net.Studio.Tests.Unit;

public sealed class JsonWorkspaceStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "grpcn-ws-tests-" + Guid.NewGuid().ToString("N"));

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
}
