using GrpCurl.Net.Studio.ViewModels.Models.Connections;

namespace GrpCurl.Net.Studio.Tests.Unit;

public sealed class ConnectionModelTests
{
    [Fact]
    public void Clone_copies_fields_with_a_fresh_id_and_independent_headers()
    {
        var original = new SavedConnection
        {
            Name = "prod",
            Address = "api:443",
            Transport = TransportMode.Tls,
            ConnectTimeout = "10s",
            Keepalive = new KeepaliveSettings { Time = "60s", Timeout = "30s" },
            Authority = "edge",
            ServerName = "sni",
            TlsProfileId = "profile-1",
            UserAgent = "ua",
            ReflectionHeaders = [new HeaderEntry { Name = "authorization", Value = "Bearer x", IsBin = false }],
            Notes = "note"
        };

        var clone = original.Clone();

        clone.Id.ShouldNotBe(original.Id);
        clone.Name.ShouldBe("prod");
        clone.Address.ShouldBe("api:443");
        clone.Transport.ShouldBe(TransportMode.Tls);
        clone.ConnectTimeout.ShouldBe("10s");
        clone.Keepalive.Time.ShouldBe("60s");
        clone.Authority.ShouldBe("edge");
        clone.ServerName.ShouldBe("sni");
        clone.TlsProfileId.ShouldBe("profile-1");
        clone.UserAgent.ShouldBe("ua");
        clone.Notes.ShouldBe("note");

        // Headers are deep-copied, not shared.
        clone.ReflectionHeaders.Single().Value = "mutated";
        original.ReflectionHeaders.Single().Value.ShouldBe("Bearer x");
    }

    [Fact]
    public void TestConnectionResult_success_singular_and_plural_messages()
    {
        TestConnectionResult.Success(1).Message.ShouldContain("1 service via");
        TestConnectionResult.Success(3).Message.ShouldContain("3 services via");
        TestConnectionResult.Success(5).ServiceCount.ShouldBe(5);
        TestConnectionResult.Success(2).Ok.ShouldBeTrue();
    }

    [Fact]
    public void TestConnectionResult_failure_carries_message_and_no_count()
    {
        var failure = TestConnectionResult.Failure("nope");

        failure.Ok.ShouldBeFalse();
        failure.ServiceCount.ShouldBeNull();
        failure.Message.ShouldBe("nope");
    }

    [Fact]
    public void Workspace_empty_starts_at_schema_version_1_with_no_connections()
    {
        var ws = WorkspaceModel.Empty();

        ws.SchemaVersion.ShouldBe(1);
        ws.Connections.ShouldBeEmpty();
    }
}
