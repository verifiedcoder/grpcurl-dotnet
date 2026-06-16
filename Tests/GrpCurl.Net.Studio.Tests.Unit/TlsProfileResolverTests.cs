using GrpCurl.Net.Studio.Services;
using GrpCurl.Net.Studio.TestSupport;
using GrpCurl.Net.Studio.ViewModels.Models.Connections;

namespace GrpCurl.Net.Studio.Tests.Unit;

/// <summary>
///     L1 unit tests for <see cref="TlsProfileResolver" />: the rules that decide whether a connection
///     carries TLS profile material and where the PKCS12 password comes from (SEC-017). No network.
/// </summary>
public sealed class TlsProfileResolverTests
{
    private static (TlsProfileResolver Resolver, FakeSecretStore Secrets) Build(params TlsProfile[] profiles)
    {
        var workspace = new FakeWorkspaceStore(new WorkspaceModel { TlsProfiles = [.. profiles] });
        var secrets = new FakeSecretStore();
        return (new TlsProfileResolver(workspace, secrets), secrets);
    }

    [Fact]
    public async Task Plaintext_connection_resolves_no_profile()
    {
        var profile = new TlsProfile { Name = "p", CaCertPath = "/ca.pem" };
        var (resolver, _) = Build(profile);
        var connection = new SavedConnection { Transport = TransportMode.Plaintext, TlsProfileId = profile.Id };

        var (resolved, password) = await resolver.ResolveAsync(connection, TestContext.Current.CancellationToken);

        resolved.ShouldBeNull();
        password.ShouldBeNull();
    }

    [Fact]
    public async Task Tls_connection_without_a_profile_reference_resolves_nothing()
    {
        var (resolver, _) = Build();
        var connection = new SavedConnection { Transport = TransportMode.Tls, TlsProfileId = null };

        var (resolved, _) = await resolver.ResolveAsync(connection, TestContext.Current.CancellationToken);

        resolved.ShouldBeNull();
    }

    [Fact]
    public async Task Dangling_profile_reference_falls_back_to_system_default()
    {
        var (resolver, _) = Build();
        var connection = new SavedConnection { Transport = TransportMode.Tls, TlsProfileId = "does-not-exist" };

        var (resolved, _) = await resolver.ResolveAsync(connection, TestContext.Current.CancellationToken);

        resolved.ShouldBeNull();
    }

    [Fact]
    public async Task Profile_without_a_password_ref_resolves_with_a_null_password()
    {
        var profile = new TlsProfile { Name = "pem", ClientCertPath = "/c.pem", ClientKeyPath = "/k.pem" };
        var (resolver, _) = Build(profile);
        var connection = new SavedConnection { Transport = TransportMode.Tls, TlsProfileId = profile.Id };

        var (resolved, password) = await resolver.ResolveAsync(connection, TestContext.Current.CancellationToken);

        resolved.ShouldBe(profile);
        password.ShouldBeNull();
    }

    [Fact]
    public async Task Pkcs12_password_is_fetched_from_the_secret_store()
    {
        var profile = new TlsProfile { Name = "pfx", ClientCertPath = "/c.pfx", ClientCertPasswordSecretRef = "secret-key" };
        var (resolver, secrets) = Build(profile);
        await secrets.SetAsync("secret-key", "p@ss", TestContext.Current.CancellationToken);
        var connection = new SavedConnection { Transport = TransportMode.Tls, TlsProfileId = profile.Id };

        var (resolved, password) = await resolver.ResolveAsync(connection, TestContext.Current.CancellationToken);

        resolved.ShouldBe(profile);
        password.ShouldBe("p@ss");
    }
}
