using GrpCurl.Net.Studio.Services;
using GrpCurl.Net.Studio.ViewModels.Models.GraphQl;
using System.Text.Json.Nodes;

namespace GrpCurl.Net.Studio.Tests.Unit;

/// <summary>
///     GQL-070/073: the response <c>errors[]</c> are parsed into structured entries and classified by kind
///     (upstream gRPC failure vs configuration/usage), not by trusting <c>extensions.code</c>.
/// </summary>
public sealed class GraphQlServiceErrorTests
{
    [Fact]
    public void Errors_are_parsed_with_path_extensions_and_classification()
    {
        var envelope = JsonNode.Parse(
            """
            {
              "data": { "a": null },
              "errors": [
                { "message": "boom", "path": ["a", 0, "b"],
                  "extensions": { "code": "UPSTREAM_ERROR", "grpcStatus": "InvalidArgument", "grpcStatusCode": 3 } },
                { "message": "No mapping for field x", "extensions": { "code": "INTERNAL_ERROR" } },
                { "message": "bare" }
              ]
            }
            """)!.AsObject();

        var errors = GraphQlService.ParseErrors(envelope);

        errors.Count.ShouldBe(3);

        // Upstream: carries a gRPC status.
        errors[0].Class.ShouldBe(GraphQlErrorClass.Upstream);
        errors[0].IsUpstream.ShouldBeTrue();
        errors[0].PathText.ShouldBe("a › 0 › b");
        errors[0].GrpcStatusCode.ShouldBe(3);
        errors[0].ExtensionsText.ShouldContain("InvalidArgument (3)");

        // Coded but no gRPC status → configuration kind (GQL-073: classify by kind, not code).
        errors[1].Class.ShouldBe(GraphQlErrorClass.Configuration);

        // No extensions at all → unknown.
        errors[2].Class.ShouldBe(GraphQlErrorClass.Unknown);
        errors[2].HasPath.ShouldBeFalse();
    }

    [Fact]
    public void A_clean_envelope_has_no_errors()
        => GraphQlService.ParseErrors(JsonNode.Parse("""{ "data": { "a": 1 } }""")!.AsObject()).ShouldBeEmpty();
}
