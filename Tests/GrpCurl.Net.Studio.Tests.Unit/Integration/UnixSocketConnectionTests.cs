using GrpCurl.Net.Studio.Services;
using GrpCurl.Net.Studio.Tests.Unit.Fixtures;
using GrpCurl.Net.Studio.ViewModels.Models.Connections;
using GrpCurl.Net.Studio.ViewModels.Models.Invocation;
using GrpCurl.Net.Studio.ViewModels.Services;

namespace GrpCurl.Net.Studio.Tests.Unit.Integration;

/// <summary>
///     L2 proof that a <c>unix:///path</c> connection works end-to-end through the Studio service layer
///     → Core → an in-process Kestrel Unix-socket server (FR-011). Linux/macOS only.
/// </summary>
[Collection(StudioUdsServerCollection.Name)]
public sealed class UnixSocketConnectionTests(StudioUdsServerFixture server)
{
    private SavedConnection Conn() => new()
    {
        Name = "uds",
        Address = server.Address,
        Transport = TransportMode.Plaintext,
        DescriptorSource = new DescriptorSourceConfig()
    };

    [Fact]
    public async Task Reflection_lists_services_over_a_unix_socket()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Unix domain sockets are not supported on Windows.");

        var result = await new ConnectionRegistry().TestConnectionAsync(Conn(), TestContext.Current.CancellationToken);

        result.Ok.ShouldBeTrue(result.Message);
        result.ServiceCount!.Value.ShouldBeGreaterThan(0);
    }

    [Fact]
    public async Task Unary_invoke_succeeds_over_a_unix_socket()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Unix domain sockets are not supported on Windows.");

        var runner = new InvocationRunner(new InvocationService());
        var request = new InvocationRequestModel(Conn(), "testing.TestService/EmptyCall", "{}", []);

        var result = await runner.InvokeUnaryAsync(request, TestContext.Current.CancellationToken);

        result.Ok.ShouldBeTrue(result.ErrorMessage);
        result.Status.Code.ShouldBe(0);
    }
}
