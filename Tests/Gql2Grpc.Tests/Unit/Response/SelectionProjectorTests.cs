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
        // Arrange
        var source = JsonNode.Parse("""{"id":"1","name":"x","hidden":"no"}""");
        var selections = new[] { Leaf("id"), Leaf("name") };
        var errors = new List<GraphQLError>();

        // Act
        var result = Projector.Project(source, selections, null, [], errors)!.AsObject();

        // Assert
        result["id"]!.GetValue<string>().ShouldBe("1");
        result["name"]!.GetValue<string>().ShouldBe("x");
        result.ContainsKey("hidden").ShouldBeFalse();
    }

    [Fact]
    public void Applies_alias_as_output_key()
    {
        // Arrange
        var source = JsonNode.Parse("""{"id":"1"}""");
        var selections = new[]
        {
            new ResolvedSelection("identifier", "id", new Dictionary<string, JsonNode?>(), [])
        };
        var errors = new List<GraphQLError>();

        // Act
        var result = Projector.Project(source, selections, null, [], errors)!.AsObject();

        // Assert
        result["identifier"]!.GetValue<string>().ShouldBe("1");
    }

    [Fact]
    public void Reads_snake_case_source_key_for_camel_case_selection()
    {
        // Arrange
        var source = JsonNode.Parse("""{"first_name":"Alex"}""");
        var selections = new[] { Leaf("firstName") };
        var errors = new List<GraphQLError>();

        // Act
        var result = Projector.Project(source, selections, null, [], errors)!.AsObject();

        // Assert
        result["firstName"]!.GetValue<string>().ShouldBe("Alex");
    }

    [Fact]
    public void Projects_each_array_element()
    {
        // Arrange
        var source = JsonNode.Parse("""[{"id":"1"},{"id":"2"}]""");
        var selections = new[] { Leaf("id") };
        var errors = new List<GraphQLError>();

        // Act
        var result = Projector.Project(source, selections, null, [], errors)!.AsArray();

        // Assert
        result.Count.ShouldBe(2);
        result[0]!.AsObject()["id"]!.GetValue<string>().ShouldBe("1");
    }

    [Fact]
    public void Unwrap_hint_strips_wrapper_before_projection()
    {
        // Arrange
        var source = JsonNode.Parse("""{"items":[{"id":"1"}]}""");
        var selections = new[] { Leaf("id") };
        var errors = new List<GraphQLError>();

        // Act
        var result = Projector.Project(source, selections, new ResponseShaping { Unwrap = "items" }, [], errors)!.AsArray();

        // Assert
        result.Count.ShouldBe(1);
        result[0]!.AsObject()["id"]!.GetValue<string>().ShouldBe("1");
    }

    [Fact]
    public void Strict_mode_reports_missing_fields_as_errors()
    {
        // Arrange
        var strict = new SelectionProjector(strict: true);
        var source = JsonNode.Parse("""{"id":"1"}""");
        var selections = new[] { Leaf("missing") };
        var errors = new List<GraphQLError>();

        // Act
        strict.Project(source, selections, null, [], errors);

        // Assert
        errors.Count.ShouldBe(1);
        errors[0].Message.ShouldContain("missing");
    }

    private static ResolvedSelection Leaf(string name) =>
        new(name, name, new Dictionary<string, JsonNode?>(), []);
}
