using GrpCurl.Net.Studio.Services;
using GrpCurl.Net.Studio.Tests.Unit.Fixtures;
using GrpCurl.Net.Studio.ViewModels.Models.Connections;
using GrpCurl.Net.Studio.ViewModels.Models.Descriptors;
using GrpCurl.Net.Studio.ViewModels.Services;

namespace GrpCurl.Net.Studio.Tests.Unit.Integration;

/// <summary>
///     L2 service-layer E2E: drives the real <see cref="DescriptorService" /> against the
///     in-process TestServer, proving the explorer's catalog-load path (GUI → service → Core →
///     real gRPC reflection) end to end, including the four streaming shapes.
/// </summary>
[Collection(StudioPlaintextServerCollection.Name)]
public sealed class DescriptorServiceTests(StudioPlaintextServerFixture server)
{
    private static SavedConnection PlaintextReflection(string address) => new()
    {
        Name = "test",
        Address = address,
        Transport = TransportMode.Plaintext,
        DescriptorMode = DescriptorMode.Reflection
    };

    [Fact]
    public async Task Load_against_live_server_returns_the_test_service_with_all_four_shapes()
    {
        IDescriptorService descriptors = new DescriptorService();

        var result = await descriptors.LoadAsync(
            PlaintextReflection(server.Address),
            TestContext.Current.CancellationToken);

        result.Ok.ShouldBeTrue(result.Error?.Message);
        result.Catalog.ShouldNotBeNull();

        var testService = result.Catalog!.Services.Single(s => s.FullName == "testing.TestService");

        // test.proto declares one of each shape on TestService.
        var shapes = testService.Methods.Select(m => m.Shape).ToHashSet();
        shapes.ShouldContain(StreamingShape.Unary);            // UnaryCall / EmptyCall
        shapes.ShouldContain(StreamingShape.ServerStreaming);  // StreamingOutputCall
        shapes.ShouldContain(StreamingShape.ClientStreaming);  // StreamingInputCall
        shapes.ShouldContain(StreamingShape.BidiStreaming);    // FullDuplexCall / HalfDuplexCall

        var unary = testService.Methods.First(m => m.Name == "UnaryCall");
        unary.FullName.ShouldBe("testing.TestService/UnaryCall");
        unary.InputType.ShouldBe("testing.SimpleRequest");
        unary.OutputType.ShouldBe("testing.SimpleResponse");
    }

    [Fact]
    public async Task Load_surfaces_the_unimplemented_service_node()
    {
        IDescriptorService descriptors = new DescriptorService();

        var result = await descriptors.LoadAsync(
            PlaintextReflection(server.Address),
            TestContext.Current.CancellationToken);

        result.Ok.ShouldBeTrue(result.Error?.Message);
        result.Catalog!.Services.ShouldContain(s => s.FullName == "testing.UnimplementedService");
    }

    [Fact]
    public async Task Load_to_dead_address_returns_a_failure_result()
    {
        IDescriptorService descriptors = new DescriptorService();

        // Nothing listening on loopback port 1 → connection refused, mapped to a failure result.
        var result = await descriptors.LoadAsync(
            PlaintextReflection("localhost:1"),
            TestContext.Current.CancellationToken);

        result.Ok.ShouldBeFalse();
        result.Error.ShouldNotBeNull();
        result.Error!.Message.ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Load_honours_user_cancellation()
    {
        IDescriptorService descriptors = new DescriptorService();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(
            async () => await descriptors.LoadAsync(PlaintextReflection(server.Address), cts.Token));
    }
}
