using System.Text.Json.Nodes;
using Gql2Grpc.Configuration;
using Gql2Grpc.GraphQL;
using Gql2Grpc.Translation;

namespace Gql2Grpc.Tests.Unit.Translation;

public sealed class JsonRequestTranslatorTests
{
    private readonly JsonRequestTranslator _translator = new();
    private readonly MappingDefaults _defaults = new();

    private static readonly string[] Expected = ["id", "payload.body"];

    [Fact]
    public void Rename_maps_arg_to_target_field()
    {
        // Arrange
        var entry = new MappingEntry
        {
            GraphqlField = "foo",
            OperationType = GraphQLOperationType.Query,
            Service = "s",
            Method = "M",
            Arguments = new Dictionary<string, ArgumentRule>
            {
                ["first"] = new ArgumentRule.Rename("page_size")
            }
        };

        var selection = new ResolvedSelection(
            "foo", "foo",
            new Dictionary<string, JsonNode?> { ["first"] = JsonValue.Create(10) },
            []);

        var json = _translator.Translate(selection, entry, _defaults);

        // Act
        var parsed = JsonNode.Parse(json)!.AsObject();

        // Assert
        parsed["page_size"]!.GetValue<int>().ShouldBe(10);
    }

    [Fact]
    public void Path_rule_sets_nested_field()
    {
        // Arrange
        var entry = new MappingEntry
        {
            GraphqlField = "foo",
            OperationType = GraphQLOperationType.Query,
            Service = "s",
            Method = "M",
            Arguments = new Dictionary<string, ArgumentRule>
            {
                ["first"] = new ArgumentRule.PathRule("page.size")
            }
        };

        var selection = new ResolvedSelection("foo", "foo",
            new Dictionary<string, JsonNode?> { ["first"] = JsonValue.Create(5) },
            []);

        var json = _translator.Translate(selection, entry, _defaults);

        // Act
        var root = JsonNode.Parse(json)!.AsObject();

        // Assert
        root["page"]!.AsObject()["size"]!.GetValue<int>().ShouldBe(5);
    }

    [Fact]
    public void Spread_path_dot_spreads_object_onto_root()
    {
        // Arrange
        var entry = new MappingEntry
        {
            GraphqlField = "foo",
            OperationType = GraphQLOperationType.Mutation,
            Service = "s",
            Method = "M",
            Arguments = new Dictionary<string, ArgumentRule>
            {
                ["input"] = new ArgumentRule.PathRule(".")
            }
        };

        var inputObj = new JsonObject { ["a"] = 1, ["b"] = "hello" };
        var selection = new ResolvedSelection("foo", "foo",
            new Dictionary<string, JsonNode?> { ["input"] = inputObj },
            []);

        var json = _translator.Translate(selection, entry, _defaults);

        // Act
        var root = JsonNode.Parse(json)!.AsObject();

        // Assert
        root["a"]!.GetValue<int>().ShouldBe(1);
        root["b"]!.GetValue<string>().ShouldBe("hello");
    }

    [Fact]
    public void Literal_is_always_applied_even_without_caller_input()
    {
        // Arrange
        var entry = new MappingEntry
        {
            GraphqlField = "foo",
            OperationType = GraphQLOperationType.Query,
            Service = "s",
            Method = "M",
            Arguments = new Dictionary<string, ArgumentRule>
            {
                ["tenantId"] = new ArgumentRule.Literal("acme")
            }
        };

        var selection = new ResolvedSelection("foo", "foo",
            new Dictionary<string, JsonNode?>(),
            []);

        // Act
        var json = _translator.Translate(selection, entry, _defaults);

        // Assert
        JsonNode.Parse(json)!.AsObject()["tenant_id"]!.GetValue<string>().ShouldBe("acme");
    }

    [Fact]
    public void Selection_fieldmask_is_generated()
    {
        // Arrange
        var entry = new MappingEntry
        {
            GraphqlField = "foo",
            OperationType = GraphQLOperationType.Query,
            Service = "s",
            Method = "M",
            SelectionFieldMaskPath = "read_mask"
        };

        var selection = new ResolvedSelection("foo", "foo",
            new Dictionary<string, JsonNode?>(),
            [
                new ResolvedSelection("id", "id", new Dictionary<string, JsonNode?>(), []),
                new ResolvedSelection("payload", "payload", new Dictionary<string, JsonNode?>(),
                [
                    new ResolvedSelection("body", "body", new Dictionary<string, JsonNode?>(), [])
                ])
            ]);

        var json = _translator.Translate(selection, entry, _defaults);

        // Act
        var mask = JsonNode.Parse(json)!.AsObject()["read_mask"]!.GetValue<string>();

        // Assert
        mask.Split(',').ShouldBe(Expected);
    }

    [Fact]
    public void Convention_fallback_uses_snake_case()
    {
        // Arrange
        var entry = new MappingEntry
        {
            GraphqlField = "foo",
            OperationType = GraphQLOperationType.Query,
            Service = "s",
            Method = "M"
        };

        var selection = new ResolvedSelection("foo", "foo",
            new Dictionary<string, JsonNode?> { ["userId"] = JsonValue.Create("abc") },
            []);

        // Act
        var json = _translator.Translate(selection, entry, _defaults);

        // Assert
        JsonNode.Parse(json)!.AsObject()["user_id"]!.GetValue<string>().ShouldBe("abc");
    }
}
