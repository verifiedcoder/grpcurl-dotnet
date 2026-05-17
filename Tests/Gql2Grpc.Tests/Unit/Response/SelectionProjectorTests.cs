using System.Text.Json.Nodes;
using Gql2Grpc.Configuration;
using Gql2Grpc.GraphQL;
using Gql2Grpc.Response;

namespace Gql2Grpc.Tests.Unit.Response;

public sealed class SelectionProjectorTests
{
    private static readonly SelectionProjector Projector = new(strict: false);

    [Fact]
    public void Projects_only_requested_fields()
    {
        var source = JsonNode.Parse("""{"id":"1","name":"x","hidden":"no"}""");
        var selections = new[] { Leaf("id"), Leaf("name") };
        var errors = new List<GraphQLError>();

        var result = Projector.Project(source, selections, null, [], errors)!.AsObject();

        result["id"]!.GetValue<string>().ShouldBe("1");
        result["name"]!.GetValue<string>().ShouldBe("x");
        result.ContainsKey("hidden").ShouldBeFalse();
    }

    [Fact]
    public void Applies_alias_as_output_key()
    {
        var source = JsonNode.Parse("""{"id":"1"}""");
        var selections = new[]
        {
            new ResolvedSelection("identifier", "id", new Dictionary<string, JsonNode?>(), [])
        };
        var errors = new List<GraphQLError>();

        var result = Projector.Project(source, selections, null, [], errors)!.AsObject();
        result["identifier"]!.GetValue<string>().ShouldBe("1");
    }

    [Fact]
    public void Reads_snake_case_source_key_for_camel_case_selection()
    {
        var source = JsonNode.Parse("""{"first_name":"Alex"}""");
        var selections = new[] { Leaf("firstName") };
        var errors = new List<GraphQLError>();

        var result = Projector.Project(source, selections, null, [], errors)!.AsObject();
        result["firstName"]!.GetValue<string>().ShouldBe("Alex");
    }

    [Fact]
    public void Projects_each_array_element()
    {
        var source = JsonNode.Parse("""[{"id":"1"},{"id":"2"}]""");
        var selections = new[] { Leaf("id") };
        var errors = new List<GraphQLError>();

        var result = Projector.Project(source, selections, null, [], errors)!.AsArray();
        result.Count.ShouldBe(2);
        result[0]!.AsObject()["id"]!.GetValue<string>().ShouldBe("1");
    }

    [Fact]
    public void Unwrap_hint_strips_wrapper_before_projection()
    {
        var source = JsonNode.Parse("""{"items":[{"id":"1"}]}""");
        var selections = new[] { Leaf("id") };
        var errors = new List<GraphQLError>();

        var result = Projector.Project(source, selections, new ResponseShaping { Unwrap = "items" }, [], errors)!.AsArray();
        result.Count.ShouldBe(1);
        result[0]!.AsObject()["id"]!.GetValue<string>().ShouldBe("1");
    }

    [Fact]
    public void Strict_mode_reports_missing_fields_as_errors()
    {
        var strict = new SelectionProjector(strict: true);
        var source = JsonNode.Parse("""{"id":"1"}""");
        var selections = new[] { Leaf("missing") };
        var errors = new List<GraphQLError>();

        strict.Project(source, selections, null, [], errors);
        errors.Count.ShouldBe(1);
        errors[0].Message.ShouldContain("missing");
    }

    private static ResolvedSelection Leaf(string name) =>
        new(name, name, new Dictionary<string, JsonNode?>(), []);
}
