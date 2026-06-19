using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Google.Rpc;
using Grpc.Core;
using GrpCurl.Net.Invocation;
using GrpCurl.Net.Studio.Services;
using GrpCurl.Net.Studio.ViewModels.Models.Invocation;
using GrpCurl.Net.Studio.ViewModels.Services;
using System.Text.Json;

namespace GrpCurl.Net.Studio.Tests.Unit;

/// <summary>
///     L1 unit tests for <see cref="ErrorMapper" />: synthetic <c>google.rpc.*</c> details → the typed
///     <see cref="ErrorDetailModel" /> hierarchy, FR-091 severity grouping, FR-098 deadline
///     disambiguation, and the FR-099 JSON-envelope shape (CLI parity).
/// </summary>
public sealed class ErrorMapperTests
{
    private static readonly ErrorContext Ctx = new("testing.TestService/UnaryCall", "localhost:5000", DeadlineSet: false);

    private static StatusDetail Detail(string shortType, IMessage message)
        => new($"type.googleapis.com/{shortType}", message.ToByteArray(), message);

    private static UnaryOutcome FailedOutcome(int code, string codeName, string detail, params StatusDetail[] details)
        => new(false, new Metadata(), Response: null, ResponseTrailers: null,
            new InvocationStatus(code, codeName, detail),
            details.Length == 0 ? null : new StatusDetails(code, detail, details));

    [Fact]
    public void BadRequest_detail_maps_to_field_violations()
    {
        var bad = new BadRequest
        {
            FieldViolations =
            {
                new BadRequest.Types.FieldViolation { Field = "name", Description = "must not be empty" }
            }
        };

        var model = ErrorMapper.FromOutcome(
            FailedOutcome(3, "InvalidArgument", "bad request", Detail("google.rpc.BadRequest", bad)), Ctx);

        var detail = model.Details.ShouldHaveSingleItem().ShouldBeOfType<BadRequestDetail>();
        detail.Violations.ShouldHaveSingleItem().Field.ShouldBe("name");
        detail.Violations[0].Description.ShouldBe("must not be empty");
    }

    [Fact]
    public void RetryInfo_detail_maps_to_a_timespan()
    {
        var retry = new RetryInfo { RetryDelay = Duration.FromTimeSpan(TimeSpan.FromSeconds(2.5)) };

        var model = ErrorMapper.FromOutcome(
            FailedOutcome(14, "Unavailable", "try later", Detail("google.rpc.RetryInfo", retry)), Ctx);

        model.Details.ShouldHaveSingleItem().ShouldBeOfType<RetryInfoDetail>()
            .Delay.ShouldBe(TimeSpan.FromSeconds(2.5));
    }

    [Fact]
    public void ErrorInfo_detail_carries_reason_domain_and_metadata()
    {
        var info = new ErrorInfo { Reason = "API_DISABLED", Domain = "example.com", Metadata = { { "service", "foo" } } };

        var model = ErrorMapper.FromOutcome(
            FailedOutcome(9, "FailedPrecondition", "disabled", Detail("google.rpc.ErrorInfo", info)), Ctx);

        var detail = model.Details.ShouldHaveSingleItem().ShouldBeOfType<ErrorInfoDetail>();
        detail.Reason.ShouldBe("API_DISABLED");
        detail.Domain.ShouldBe("example.com");
        detail.Metadata.ShouldHaveSingleItem().Name.ShouldBe("service");
    }

    [Fact]
    public void QuotaFailure_and_precondition_and_help_and_localized_message_each_map()
    {
        var quota = new QuotaFailure { Violations = { new QuotaFailure.Types.Violation { Subject = "user", Description = "limit" } } };
        var precondition = new PreconditionFailure { Violations = { new PreconditionFailure.Types.Violation { Type = "TOS", Subject = "tos", Description = "accept" } } };
        var help = new Help { Links = { new Help.Types.Link { Description = "docs", Url = "https://example.com/help" } } };
        var localized = new LocalizedMessage { Locale = "en-US", Message = "Something went wrong" };

        var model = ErrorMapper.FromOutcome(FailedOutcome(8, "ResourceExhausted", "nope",
            Detail("google.rpc.QuotaFailure", quota),
            Detail("google.rpc.PreconditionFailure", precondition),
            Detail("google.rpc.Help", help),
            Detail("google.rpc.LocalizedMessage", localized)), Ctx);

        model.Details.OfType<QuotaFailureDetail>().Single().Violations[0].Subject.ShouldBe("user");
        model.Details.OfType<PreconditionFailureDetail>().Single().Violations[0].Type.ShouldBe("TOS");
        model.Details.OfType<HelpDetail>().Single().Links[0].Url.ShouldBe("https://example.com/help");
        model.Details.OfType<LocalizedMessageDetail>().Single().Locale.ShouldBe("en-US");
    }

    [Fact]
    public void Unknown_detail_type_falls_back_to_generic_json()
    {
        var debug = new DebugInfo { Detail = "boom", StackEntries = { "frame0" } };

        var model = ErrorMapper.FromOutcome(
            FailedOutcome(13, "Internal", "internal", Detail("google.rpc.DebugInfo", debug)), Ctx);

        var generic = model.Details.ShouldHaveSingleItem().ShouldBeOfType<GenericDetail>();
        generic.Title.ShouldBe("DebugInfo");
        generic.Json.ShouldContain("boom");
    }

    [Theory]
    [InlineData(1, StatusSeverity.Cancelled)]
    [InlineData(14, StatusSeverity.Transient)]
    [InlineData(4, StatusSeverity.Transient)]
    [InlineData(5, StatusSeverity.Caller)]
    [InlineData(7, StatusSeverity.Caller)]
    [InlineData(16, StatusSeverity.Caller)]
    [InlineData(13, StatusSeverity.Server)]
    [InlineData(12, StatusSeverity.Server)]
    public void Severity_groups_follow_FR091(int code, StatusSeverity expected)
        => StatusSeverityMap.FromCode(code).ShouldBe(expected);

    [Fact]
    public void Cancelled_with_a_deadline_set_disambiguates_as_client_deadline()
    {
        var ctx = Ctx with { DeadlineSet = true };

        var model = ErrorMapper.FromOutcome(FailedOutcome(1, "Cancelled", string.Empty), ctx);

        model.Hint.ShouldBe("Deadline reached (client).");
    }

    [Fact]
    public void Unavailable_suggests_reachability_and_plaintext()
    {
        var model = ErrorMapper.FromOutcome(FailedOutcome(14, "Unavailable", "connection refused"), Ctx);

        model.Severity.ShouldBe(StatusSeverity.Transient);
        model.Suggestions.ShouldNotBeEmpty();
        model.Suggestions.ShouldContain(s => s.Text.Contains("Plaintext"));
    }

    [Fact]
    public void Deadline_exceeded_suggestion_deep_links_to_the_network_settings_FR095()
    {
        var model = ErrorMapper.FromOutcome(FailedOutcome(4, "DeadlineExceeded", string.Empty), Ctx);

        var suggestion = model.Suggestions.ShouldHaveSingleItem();
        suggestion.HasSettingLink.ShouldBeTrue();
        suggestion.SettingLink.ShouldBe("network");
    }

    [Fact]
    public void Custom_ca_revocation_failure_suggests_offline_or_nocheck_SEC013()
    {
        var model = ErrorMapper.FromOutcome(
            FailedOutcome(14, "Unavailable", "The certificate revocation status could not be determined (RevocationStatusUnknown)."),
            Ctx);

        model.Suggestions.ShouldContain(s =>
            s.Text.Contains("revocation mode") && s.Text.Contains("offline") && s.Text.Contains("nocheck"));
    }

    [Fact]
    public void Non_revocation_failure_does_not_add_the_revocation_hint()
    {
        var model = ErrorMapper.FromOutcome(FailedOutcome(14, "Unavailable", "connection refused"), Ctx);

        model.Suggestions.ShouldNotContain(s => s.Text.Contains("revocation mode"));
    }

    [Fact]
    public void Empty_detail_uses_a_default_headline()
    {
        var model = ErrorMapper.FromOutcome(FailedOutcome(5, "NotFound", string.Empty), Ctx);

        model.Headline.ShouldBe("Not found");
    }

    [Fact]
    public void Json_envelope_matches_the_cli_shape()
    {
        var bad = new BadRequest { FieldViolations = { new BadRequest.Types.FieldViolation { Field = "f", Description = "d" } } };

        var model = ErrorMapper.FromOutcome(
            FailedOutcome(3, "InvalidArgument", "bad", Detail("google.rpc.BadRequest", bad)), Ctx);

        using var doc = JsonDocument.Parse(model.JsonEnvelope);
        var root = doc.RootElement;

        root.GetProperty("kind").GetString().ShouldBe("error");
        root.GetProperty("category").GetString().ShouldBe("rpc");          // camelCase enum
        root.GetProperty("exitCode").GetInt32().ShouldBe(64 + 3);
        root.GetProperty("message").GetString().ShouldBe("bad");
        root.GetProperty("address").GetString().ShouldBe("localhost:5000");
        root.GetProperty("method").GetString().ShouldBe("testing.TestService/UnaryCall");

        var grpc = root.GetProperty("grpc");
        grpc.GetProperty("code").GetInt32().ShouldBe(3);
        grpc.GetProperty("status").GetString().ShouldBe("InvalidArgument");
        grpc.GetProperty("statusDetails").GetProperty("details")[0].GetProperty("typeUrl").GetString()
            .ShouldBe("type.googleapis.com/google.rpc.BadRequest");
    }

    [Fact]
    public void Json_envelope_omits_null_fields()
    {
        var model = ErrorMapper.FromInternal("malformed JSON", Ctx);

        using var doc = JsonDocument.Parse(model.JsonEnvelope);
        var root = doc.RootElement;

        root.GetProperty("category").GetString().ShouldBe("internal");
        root.TryGetProperty("hint", out _).ShouldBeFalse();   // ignore-null
        root.TryGetProperty("grpc", out _).ShouldBeFalse();
    }

    [Fact]
    public void Schema_failure_is_a_caller_severity_with_no_grpc_status()
    {
        var model = ErrorMapper.FromSchema("Method 'p.S/M' was not found on the server.", Ctx);

        model.Category.ShouldBe(ErrorCategoryKind.Schema);
        model.Severity.ShouldBe(StatusSeverity.Caller);
        model.Details.ShouldBeEmpty();
    }
}
