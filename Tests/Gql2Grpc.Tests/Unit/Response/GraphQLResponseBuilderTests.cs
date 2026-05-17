using System.Text.Json.Nodes;
using Gql2Grpc.GraphQL;
using Gql2Grpc.Response;

namespace Gql2Grpc.Tests.Unit.Response;

public sealed class GraphQLResponseBuilderTests
{
    [Fact]
    public void Success_envelope_has_no_errors_key()
    {
        var result = new RootFieldResult(
            "foo",
            new JsonObject { ["x"] = 1 },
            Array.Empty<GraphQLError>(),
            Failed: false);

        var envelope = GraphQLResponseBuilder.Build([result], Array.Empty<GraphQLError>());

        envelope.ContainsKey("errors").ShouldBeFalse();
        envelope["data"]!.AsObject()["foo"]!.AsObject()["x"]!.GetValue<int>().ShouldBe(1);
    }

    [Fact]
    public void Error_path_is_serialised_as_json_array()
    {
        var error = new GraphQLError("boom", new object[] { "foo", 0, "bar" });
        var envelope = GraphQLResponseBuilder.BuildSingleError(error);

        var path = envelope["errors"]!.AsArray()[0]!.AsObject()["path"]!.AsArray();
        path.Count.ShouldBe(3);
        path[0]!.GetValue<string>().ShouldBe("foo");
        path[1]!.GetValue<int>().ShouldBe(0);
        path[2]!.GetValue<string>().ShouldBe("bar");
    }

    [Fact]
    public void Extensions_are_emitted_when_present()
    {
        var error = new GraphQLError(
            "bad",
            ["foo"],
            new Dictionary<string, object?>
            {
                ["code"] = "UPSTREAM_ERROR",
                ["grpcStatusCode"] = 14
            });

        var envelope = GraphQLResponseBuilder.BuildSingleError(error);
        var ext = envelope["errors"]!.AsArray()[0]!.AsObject()["extensions"]!.AsObject();

        ext["code"]!.GetValue<string>().ShouldBe("UPSTREAM_ERROR");
        ext["grpcStatusCode"]!.GetValue<int>().ShouldBe(14);
    }

    [Fact]
    public void Partial_success_keeps_data_object_with_null_placeholder()
    {
        var ok = new RootFieldResult("ok", JsonValue.Create(1), Array.Empty<GraphQLError>(), Failed: false);
        var bad = new RootFieldResult("bad", null, new[] { new GraphQLError("nope", ["bad"]) }, Failed: true);

        var envelope = GraphQLResponseBuilder.Build([ok, bad], Array.Empty<GraphQLError>());
        var data = envelope["data"]!.AsObject();

        data["ok"]!.GetValue<int>().ShouldBe(1);
        data["bad"].ShouldBeNull();
        envelope["errors"]!.AsArray().Count.ShouldBe(1);
    }
}
