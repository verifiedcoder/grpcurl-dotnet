using GrpCurl.Net.Studio.ViewModels.Models.Invocation;
using GrpCurl.Net.Studio.ViewModels.Services;

namespace GrpCurl.Net.Studio.Tests.Unit;

/// <summary>
///     L1 tests for the verbose transcript text (FR-111) and its redaction (FR-112): the resolved
///     target/authority, headers, message counts and status render, and secret header values never
///     appear as literals.
/// </summary>
public sealed class VerboseTranscriptFormatterTests
{
    private static VerboseTranscript Transcript(int code, string codeName, params MetadataItem[] requestHeaders) => new(
        Target: "localhost:443",
        Authority: "edge.internal",
        RequestHeaders: requestHeaders,
        ResponseHeaders: [new MetadataItem("content-type", "application/grpc", false)],
        ResponseTrailers: [],
        RequestMessages: 1,
        ResponseMessages: code == 0 ? 1 : 0,
        Status: new InvocationStatusModel(code, codeName, code == 0 ? string.Empty : "boom"));

    [Fact]
    public void Renders_target_authority_counts_and_status()
    {
        var text = VerboseTranscriptFormatter.Format(Transcript(0, "OK", new MetadataItem("x-trace", "abc", false)));

        text.ShouldContain("localhost:443");
        text.ShouldContain("edge.internal");
        text.ShouldContain("x-trace: abc");
        text.ShouldContain("content-type: application/grpc");
        text.ShouldContain("1 sent, 1 received");
        text.ShouldContain("0 OK");
    }

    [Fact]
    public void Redacts_secret_header_values()
    {
        var text = VerboseTranscriptFormatter.Format(Transcript(0, "OK",
            new MetadataItem("authorization", "Bearer super-secret-token", false),
            new MetadataItem("x-public", "visible", false)));

        text.ShouldNotContain("super-secret-token"); // redacted (FR-112)
        text.ShouldContain("authorization:");         // header name still shown
        text.ShouldContain("x-public: visible");       // non-secret shown
    }

    [Fact]
    public void Renders_the_error_status_with_detail()
    {
        var text = VerboseTranscriptFormatter.Format(Transcript(5, "NotFound"));

        text.ShouldContain("5 NotFound");
        text.ShouldContain("boom");
        text.ShouldContain("1 sent, 0 received");
    }

    [Fact]
    public void Shows_none_for_empty_header_sections()
    {
        var text = VerboseTranscriptFormatter.Format(Transcript(0, "OK"));

        text.ShouldContain("(none)"); // empty request-headers section
    }
}
