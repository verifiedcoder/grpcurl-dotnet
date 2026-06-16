using GrpCurl.Net.Studio.Services;
using GrpCurl.Net.Studio.Tests.Unit.Fixtures;
using GrpCurl.Net.Studio.TestSupport;
using GrpCurl.Net.Studio.ViewModels.Models.Connections;
using GrpCurl.Net.Studio.ViewModels.Models.Invocation;
using GrpCurl.Net.Studio.ViewModels.Services;

namespace GrpCurl.Net.Studio.Tests.Unit.Integration;

/// <summary>
///     L2 service-layer E2E for the E2.2 TLS profile path: drives the real <see cref="ConnectionRegistry" />
///     and <see cref="InvocationRunner" /> — through a real <see cref="TlsProfileResolver" /> and Core's
///     channel factory — against an in-process mTLS server that requires a client certificate. Proves the
///     GUI's profile reference actually configures custom CA + client cert/key on the wire, and that a
///     wrong CA is rejected. The test CA has no CRL/OCSP endpoint, so the profile uses
///     <c>revocation-mode = nocheck</c> (matching the CLI mTLS tests).
/// </summary>
[Collection(StudioMTlsServerCollection.Name)]
public sealed class MTlsProfileTests(StudioMTlsServerFixture server)
{
    private static (IConnectionRegistry Registry, IInvocationRunner Runner, SavedConnection Connection) Wire(
        StudioMTlsServerFixture server, string caCertPath)
    {
        var profile = new TlsProfile
        {
            Name = "mtls",
            CaCertPath = caCertPath,
            ClientCertPath = server.ClientCertPath,
            ClientKeyPath = server.ClientKeyPath,
            RevocationMode = "nocheck"
        };

        var workspace = new FakeWorkspaceStore(new WorkspaceModel { TlsProfiles = [profile] });
        var resolver = new TlsProfileResolver(workspace, new FakeSecretStore());

        var connection = new SavedConnection
        {
            Name = "mtls",
            Address = server.Address,
            Transport = TransportMode.Tls,
            ServerName = "localhost",
            ConnectTimeout = "10s",
            TlsProfileId = profile.Id,
            DescriptorMode = DescriptorMode.Reflection
        };

        return (new ConnectionRegistry(resolver), new InvocationRunner(new InvocationService(), resolver), connection);
    }

    [Fact]
    public async Task Profile_with_ca_and_client_cert_completes_the_reflection_probe()
    {
        var (registry, _, connection) = Wire(server, server.CaCertPath);

        var result = await registry.TestConnectionAsync(connection, TestContext.Current.CancellationToken);

        result.Ok.ShouldBeTrue(result.Message);
        result.ServiceCount.ShouldNotBeNull();
        result.ServiceCount!.Value.ShouldBeGreaterThan(0);
    }

    [Fact]
    public async Task Profile_with_ca_and_client_cert_invokes_a_unary_call()
    {
        var (_, runner, connection) = Wire(server, server.CaCertPath);
        var request = new InvocationRequestModel(connection, "testing.TestService/EmptyCall", "{}", []);

        var result = await runner.InvokeUnaryAsync(request, TestContext.Current.CancellationToken);

        result.Ok.ShouldBeTrue(result.ErrorMessage);
        result.Status.Code.ShouldBe(0);
    }

    [Fact]
    public async Task Profile_with_the_wrong_ca_is_rejected()
    {
        var (registry, _, connection) = Wire(server, server.WrongCaCertPath);

        var result = await registry.TestConnectionAsync(connection, TestContext.Current.CancellationToken);

        result.Ok.ShouldBeFalse();
    }
}
