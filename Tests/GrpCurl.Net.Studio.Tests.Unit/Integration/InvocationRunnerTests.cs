using GrpCurl.Net.Studio.Services;
using GrpCurl.Net.Studio.Tests.Unit.Fixtures;
using GrpCurl.Net.Studio.ViewModels.Models.Connections;
using GrpCurl.Net.Studio.ViewModels.Models.Invocation;
using GrpCurl.Net.Studio.ViewModels.Services;

namespace GrpCurl.Net.Studio.Tests.Unit.Integration;

/// <summary>
///     L2 service-layer E2E for unary invoke (FR-060..): drives the real <see cref="InvocationRunner" />
///     (over the real <see cref="InvocationService" />) against the in-process TestServer — the full
///     GUI-service → Core → real gRPC path.
/// </summary>
[Collection(StudioPlaintextServerCollection.Name)]
public sealed class InvocationRunnerTests(StudioPlaintextServerFixture server)
{
    private static IInvocationRunner Runner() => new InvocationRunner(new InvocationService());

    private static SavedConnection Conn(string address) => new()
    {
        Name = "test",
        Address = address,
        Transport = TransportMode.Plaintext,
        DescriptorMode = DescriptorMode.Reflection
    };

    private InvocationRequestModel Request(string method, string json, params HeaderEntry[] headers) => new(
        Conn(server.Address), method, json, headers, Deadline: null);

    [Fact]
    public async Task Unary_call_succeeds_and_returns_a_response()
    {
        var result = await Runner().InvokeUnaryAsync(
            Request("testing.TestService/UnaryCall", """{ "response_size": 4, "response_type": "COMPRESSABLE" }"""),
            TestContext.Current.CancellationToken);

        result.Ok.ShouldBeTrue(result.ErrorMessage);
        result.Status.Code.ShouldBe(0);
        result.ResponseJson.ShouldNotBeNullOrWhiteSpace();
        result.ResponseJson!.ShouldContain("payload");
        result.Timing.ResponseBytes.ShouldBeGreaterThan(0);
    }

    [Fact]
    public async Task Response_status_error_is_captured_not_thrown()
    {
        var result = await Runner().InvokeUnaryAsync(
            Request("testing.TestService/UnaryCall", """{ "response_status": { "code": 5, "message": "missing" } }"""),
            TestContext.Current.CancellationToken);

        result.Ok.ShouldBeFalse();
        result.Status.Code.ShouldBe(5); // NotFound
        result.ErrorMessage.ShouldBe("missing");
    }

    [Fact]
    public async Task Unknown_method_returns_a_failure()
    {
        var result = await Runner().InvokeUnaryAsync(
            Request("testing.TestService/NoSuchMethod", "{}"),
            TestContext.Current.CancellationToken);

        result.Ok.ShouldBeFalse();
        result.ErrorMessage.ShouldNotBeNull();
        result.ErrorMessage!.ShouldContain("not found");
    }

    [Fact]
    public async Task Empty_call_round_trips_with_a_binary_header()
    {
        var result = await Runner().InvokeUnaryAsync(
            Request("testing.TestService/EmptyCall", "{}", new HeaderEntry { Name = "custom-bin", Value = "AAEC", IsBin = true }),
            TestContext.Current.CancellationToken);

        result.Ok.ShouldBeTrue(result.ErrorMessage);
        result.ResponseHeaders.ShouldNotBeNull();
    }

    [Fact]
    public async Task Cancellation_is_honoured()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(
            async () => await Runner().InvokeUnaryAsync(Request("testing.TestService/EmptyCall", "{}"), cts.Token));
    }
}
