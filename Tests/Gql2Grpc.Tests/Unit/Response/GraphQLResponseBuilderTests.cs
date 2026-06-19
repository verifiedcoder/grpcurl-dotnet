using Gql2Grpc.GraphQL;
using Gql2Grpc.Response;
using System.Text.Json.Nodes;

namespace Gql2Grpc.Tests.Unit.Response;

// ReSharper disable once InconsistentNaming
public sealed class GraphQLResponseBuilderTests
{
    [Fact]
    public void Success_envelope_has_no_errors_key()
    {
        // Arrange
        var result = new RootFieldResult(
            "foo",
            new JsonObject { ["x"] = 1 },
            [],
            Failed: false);

        // Act
        var envelope = GraphQLResponseBuilder.Build([result], []);

        // Assert
        envelope.ContainsKey("errors").ShouldBeFalse();
        envelope["data"]!.AsObject()["foo"]!.AsObject()["x"]!.GetValue<int>().ShouldBe(1);
    }

    [Fact]
    public void Error_path_is_serialised_as_json_array()
    {
        // Arrange
        var error = new GraphQLError("boom", ["foo", 0, "bar"]);
        var envelope = GraphQLResponseBuilder.BuildSingleError(error);

        // Act
        var path = envelope["errors"]!.AsArray()[0]!.AsObject()["path"]!.AsArray();

        // Assert
        path.Count.ShouldBe(3);
        path[0]!.GetValue<string>().ShouldBe("foo");
        path[1]!.GetValue<int>().ShouldBe(0);
        path[2]!.GetValue<string>().ShouldBe("bar");
    }

    [Fact]
    public void Extensions_are_emitted_when_present()
    {
        // Arrange
        var error = new GraphQLError(
            "bad",
            ["foo"],
            new Dictionary<string, object?>
            {
                ["code"] = "UPSTREAM_ERROR",
                ["grpcStatusCode"] = 14
            });

        var envelope = GraphQLResponseBuilder.BuildSingleError(error);

        // Act
        var ext = envelope["errors"]!.AsArray()[0]!.AsObject()["extensions"]!.AsObject();

        // Assert
        ext["code"]!.GetValue<string>().ShouldBe("UPSTREAM_ERROR");
        ext["grpcStatusCode"]!.GetValue<int>().ShouldBe(14);
    }

    [Fact]
    public void Partial_success_keeps_data_object_with_null_placeholder()
    {
        // Arrange
        var ok = new RootFieldResult("ok", JsonValue.Create(1), [], Failed: false);
        var bad = new RootFieldResult("bad", null, [new GraphQLError("nope", ["bad"])], Failed: true);

        var envelope = GraphQLResponseBuilder.Build([ok, bad], []);

        // Act
        var data = envelope["data"]!.AsObject();

        // Assert
        data["ok"]!.GetValue<int>().ShouldBe(1);
        data["bad"].ShouldBeNull();
        envelope["errors"]!.AsArray().Count.ShouldBe(1);
    }
}
