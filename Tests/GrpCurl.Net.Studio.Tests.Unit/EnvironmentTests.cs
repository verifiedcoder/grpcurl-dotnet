using GrpCurl.Net.Studio.Services;
using GrpCurl.Net.Studio.TestSupport;
using GrpCurl.Net.Studio.ViewModels.Models.Connections;

namespace GrpCurl.Net.Studio.Tests.Unit;

/// <summary>E3.2 PR-A: environment/variable persistence (stringOrSecret) and ${VAR} resolution.</summary>
public sealed class EnvironmentTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static WorkspaceModel WithEnvironment() => new()
    {
        Id = "w1", Name = "demo",
        Environments =
        [
            new WorkspaceEnvironment
            {
                Id = "e1", Name = "staging",
                Variables =
                [
                    new EnvironmentVariable { Name = "HOST", Value = StringOrSecret.Plain("api.example.com:443") },
                    new EnvironmentVariable { Name = "TOKEN", Value = StringOrSecret.Secret("studio/v1/ws/w1/abc") }
                ]
            }
        ]
    };

    // ── stringOrSecret persistence (FR-132) ─────────────────────────────────

    [Fact]
    public void A_plain_variable_serializes_as_a_string_and_a_secret_as_a_secret_ref()
    {
        var json = WorkspaceSerializer.Serialize(WithEnvironment());

        json.ShouldContain("\"value\": \"api.example.com:443\""); // plain literal → bare string
        json.ShouldContain("\"$secret\": \"studio/v1/ws/w1/abc\""); // secret → {"$secret":…}
        json.ShouldNotContain("\"value\": { \"value\""); // no leaked literal wrapper
    }

    [Fact]
    public void Environments_round_trip_byte_stable()
    {
        var json = WorkspaceSerializer.Serialize(WithEnvironment());

        var model = WorkspaceSerializer.Deserialize(json);

        var vars = model.Environments.Single().Variables;
        vars.Single(v => v.Name == "HOST").Value.Literal.ShouldBe("api.example.com:443");
        var token = vars.Single(v => v.Name == "TOKEN");
        token.IsSecret.ShouldBeTrue();
        token.Value.SecretRef.ShouldBe("studio/v1/ws/w1/abc");
        WorkspaceSerializer.Serialize(model).ShouldBe(json);
    }

    // ── resolution (FR-131/132/134) ─────────────────────────────────────────

    private static EnvironmentService Service(out FakeWorkspaceStore workspace, out FakeSecretStore secrets)
    {
        workspace = new FakeWorkspaceStore(WithEnvironment());
        secrets = new FakeSecretStore();
        return new EnvironmentService(workspace, secrets);
    }

    [Fact]
    public async Task With_no_active_environment_only_the_os_is_consulted()
    {
        var service = Service(out _, out _);

        (await service.ResolveAsync("HOST", Ct)).ShouldBeNull();      // not in OS, no active env
        _ = (await service.ResolveAsync("PATH", Ct)).ShouldNotBeNull();   // OS fallback
    }

    [Fact]
    public async Task The_active_environment_resolves_first()
    {
        var service = Service(out _, out _);
        service.SetActive("e1");

        (await service.ResolveAsync("HOST", Ct)).ShouldBe("api.example.com:443");
    }

    [Fact]
    public async Task A_secret_variable_resolves_through_the_secret_store()
    {
        var service = Service(out _, out var secrets);
        await secrets.SetAsync("studio/v1/ws/w1/abc", "s3cr3t", Ct);
        service.SetActive("e1");

        (await service.ResolveAsync("TOKEN", Ct)).ShouldBe("s3cr3t");
    }

    [Fact]
    public async Task Expand_substitutes_every_placeholder()
    {
        var service = Service(out _, out _);
        service.SetActive("e1");

        (await service.ExpandAsync("grpc://${HOST}/api", Ct)).ShouldBe("grpc://api.example.com:443/api");
    }

    [Fact]
    public async Task An_unresolved_variable_throws_naming_the_variable_and_environment()
    {
        var service = Service(out _, out _);
        service.SetActive("e1");

        var ex = await Should.ThrowAsync<InvalidOperationException>(() => service.ExpandAsync("${MISSING}", Ct));

        ex.Message.ShouldContain("MISSING");
        ex.Message.ShouldContain("staging");
    }

    [Fact]
    public void Set_active_raises_changed_only_on_a_real_change()
    {
        var service = Service(out _, out _);
        var changes = 0;
        service.ActiveChanged += (_, _) => changes++;

        service.SetActive("e1");
        service.SetActive("e1"); // no-op
        service.SetActive(null);

        changes.ShouldBe(2);
        service.Active.ShouldBeNull();
    }
}
