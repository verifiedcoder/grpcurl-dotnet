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
        DescriptorSource = new DescriptorSourceConfig()
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
    public async Task Response_status_error_populates_the_rich_error_model()
    {
        var result = await Runner().InvokeUnaryAsync(
            Request("testing.TestService/UnaryCall", """{ "response_status": { "code": 7, "message": "denied" } }"""),
            TestContext.Current.CancellationToken);

        result.Ok.ShouldBeFalse();
        result.Error.ShouldNotBeNull();
        result.Error!.StatusCode.ShouldBe(7); // PermissionDenied
        result.Error.Severity.ShouldBe(StatusSeverity.Caller);
        result.Error.Headline.ShouldBe("denied");
        result.Error.Category.ShouldBe(ErrorCategoryKind.Rpc);
        result.Error.JsonEnvelope.ShouldContain("\"kind\":\"error\"");
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
    public async Task Request_validator_flags_a_type_mismatch_but_accepts_valid_json()
    {
        var validator = new GrpCurl.Net.Studio.Services.RequestValidator(new InvocationService());
        var conn = Conn(server.Address);

        var bad = await validator.ValidateAsync(
            conn, "testing.TestService/UnaryCall", """{ "response_size": "not-a-number" }""", allowUnknownFields: true,
            TestContext.Current.CancellationToken);
        bad.ShouldNotBeEmpty();

        var ok = await validator.ValidateAsync(
            conn, "testing.TestService/UnaryCall", """{ "response_size": 4 }""", allowUnknownFields: true,
            TestContext.Current.CancellationToken);
        ok.ShouldBeEmpty();
    }

    [Fact]
    public async Task Cancellation_is_honoured()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(
            async () => await Runner().InvokeUnaryAsync(Request("testing.TestService/EmptyCall", "{}"), cts.Token));
    }

    [Fact]
    public async Task Header_environment_variable_is_resolved_at_send_time()
    {
        Environment.SetEnvironmentVariable("STUDIO_TEST_TOKEN", "resolved-value");

        try
        {
            var result = await Runner().InvokeUnaryAsync(
                Request("testing.TestService/EmptyCall", "{}", new HeaderEntry { Name = "x-token", Value = "${STUDIO_TEST_TOKEN}" }),
                TestContext.Current.CancellationToken);

            result.Ok.ShouldBeTrue(result.ErrorMessage);
        }
        finally
        {
            Environment.SetEnvironmentVariable("STUDIO_TEST_TOKEN", null);
        }
    }

    [Fact]
    public async Task Undefined_header_environment_variable_fails_the_call()
    {
        var result = await Runner().InvokeUnaryAsync(
            Request("testing.TestService/EmptyCall", "{}", new HeaderEntry { Name = "x-token", Value = "${STUDIO_NO_SUCH_VAR}" }),
            TestContext.Current.CancellationToken);

        result.Ok.ShouldBeFalse();
        result.ErrorMessage.ShouldNotBeNull();
        result.ErrorMessage!.ShouldContain("STUDIO_NO_SUCH_VAR");
    }
}
