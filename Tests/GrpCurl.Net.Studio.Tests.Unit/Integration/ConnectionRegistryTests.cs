using GrpCurl.Net.Studio.Services;
using GrpCurl.Net.Studio.Tests.Unit.Fixtures;
using GrpCurl.Net.Studio.ViewModels.Models.Connections;
using GrpCurl.Net.Studio.ViewModels.Services;

namespace GrpCurl.Net.Studio.Tests.Unit.Integration;

/// <summary>
///     L2 service-layer E2E: drives the real <see cref="ConnectionRegistry" /> against the
///     in-process TestServer, proving the connection-probe path (GUI → service → Core →
///     real gRPC) end to end.
/// </summary>
[Collection(StudioPlaintextServerCollection.Name)]
public sealed class ConnectionRegistryTests(StudioPlaintextServerFixture server)
{
    private static SavedConnection PlaintextReflection(string address) => new()
    {
        Name = "test",
        Address = address,
        Transport = TransportMode.Plaintext,
        DescriptorMode = DescriptorMode.Reflection
    };

    [Fact]
    public async Task Test_connection_against_live_server_reports_services()
    {
        IConnectionRegistry registry = new ConnectionRegistry();

        var result = await registry.TestConnectionAsync(
            PlaintextReflection(server.Address),
            TestContext.Current.CancellationToken);

        result.Ok.ShouldBeTrue(result.Message);
        result.ServiceCount.ShouldNotBeNull();
        result.ServiceCount!.Value.ShouldBeGreaterThanOrEqualTo(1); // testing.TestService at least
    }

    [Fact]
    public async Task Test_connection_to_dead_address_returns_failure()
    {
        IConnectionRegistry registry = new ConnectionRegistry();

        // Port 1 on loopback: nothing listening → connection refused, mapped to a failure result.
        var result = await registry.TestConnectionAsync(
            PlaintextReflection("localhost:1"),
            TestContext.Current.CancellationToken);

        result.Ok.ShouldBeFalse();
        result.Message.ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Test_connection_with_invalid_address_fails_fast()
    {
        IConnectionRegistry registry = new ConnectionRegistry();

        var result = await registry.TestConnectionAsync(
            PlaintextReflection("not a valid address"),
            TestContext.Current.CancellationToken);

        result.Ok.ShouldBeFalse();
    }

    [Fact]
    public async Task Test_connection_honours_cancellation()
    {
        IConnectionRegistry registry = new ConnectionRegistry();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(() =>
            registry.TestConnectionAsync(PlaintextReflection(server.Address), cts.Token));
    }
}
