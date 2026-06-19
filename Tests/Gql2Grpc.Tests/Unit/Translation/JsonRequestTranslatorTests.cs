using Gql2Grpc.Configuration;
using Gql2Grpc.GraphQL;
using Gql2Grpc.Translation;
using GrpCurl.Net.TestServer.Protos;
using System.Text.Json.Nodes;

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

    [Fact]
    public void Convention_validates_known_field_against_request_type()
    {
        // Arrange — response_size is a real field of testing.SimpleRequest
        var entry = ConventionEntry();
        var selection = new ResolvedSelection("foo", "foo",
            new Dictionary<string, JsonNode?> { ["responseSize"] = JsonValue.Create(8) },
            []);

        // Act
        var json = _translator.Translate(selection, entry, _defaults, SimpleRequest.Descriptor);

        // Assert
        JsonNode.Parse(json)!.AsObject()["response_size"]!.GetValue<int>().ShouldBe(8);
    }

    [Fact]
    public void Convention_unknown_top_level_argument_throws()
    {
        // Arrange — "input" matches no field of SimpleRequest (the cookbook's wrong shape)
        var entry = ConventionEntry();
        var selection = new ResolvedSelection("foo", "foo",
            new Dictionary<string, JsonNode?> { ["input"] = new JsonObject { ["responseSize"] = 8 } },
            []);

        // Act / Assert
        var ex = Should.Throw<UnknownArgumentException>(() =>
            _translator.Translate(selection, entry, _defaults, SimpleRequest.Descriptor));

        ex.ArgumentName.ShouldBe("input");
        ex.RequestTypeName.ShouldBe("testing.SimpleRequest");
        ex.Message.ShouldContain("testing.SimpleRequest");
    }

    [Fact]
    public void Convention_nested_path_descends_into_message_fields()
    {
        // Arrange — details.description exists on NestedTypesMessage; arg aliases to that path.
        var entry = new MappingEntry
        {
            GraphqlField = "foo",
            OperationType = GraphQLOperationType.Query,
            Service = "s",
            Method = "M"
        };
        var defaults = new MappingDefaults
        {
            ArgumentAliases = new Dictionary<string, string> { ["desc"] = "details.description" }
        };
        var selection = new ResolvedSelection("foo", "foo",
            new Dictionary<string, JsonNode?> { ["desc"] = JsonValue.Create("hi") },
            []);

        // Act
        var json = _translator.Translate(selection, entry, defaults, NestedTypesMessage.Descriptor);

        // Assert
        JsonNode.Parse(json)!.AsObject()["details"]!.AsObject()["description"]!.GetValue<string>().ShouldBe("hi");
    }

    [Fact]
    public void Convention_descend_into_scalar_field_throws()
    {
        // Arrange — name is a scalar; name.foo cannot descend.
        var entry = new MappingEntry
        {
            GraphqlField = "foo",
            OperationType = GraphQLOperationType.Query,
            Service = "s",
            Method = "M"
        };
        var defaults = new MappingDefaults
        {
            ArgumentAliases = new Dictionary<string, string> { ["x"] = "name.foo" }
        };
        var selection = new ResolvedSelection("foo", "foo",
            new Dictionary<string, JsonNode?> { ["x"] = JsonValue.Create("v") },
            []);

        // Act / Assert
        _ = Should.Throw<UnknownArgumentException>(() =>
            _translator.Translate(selection, entry, defaults, NestedTypesMessage.Descriptor));
    }

    [Fact]
    public void Mapped_rule_to_unknown_field_is_not_validated()
    {
        // Arrange — explicit rules are authoritative; an unknown target is left untouched
        // (the mapping file is the user's contract, validated separately at load time).
        var entry = new MappingEntry
        {
            GraphqlField = "foo",
            OperationType = GraphQLOperationType.Query,
            Service = "s",
            Method = "M",
            Arguments = new Dictionary<string, ArgumentRule>
            {
                ["whatever"] = new ArgumentRule.Rename("not_a_real_field")
            }
        };
        var selection = new ResolvedSelection("foo", "foo",
            new Dictionary<string, JsonNode?> { ["whatever"] = JsonValue.Create(1) },
            []);

        // Act
        var json = _translator.Translate(selection, entry, _defaults, SimpleRequest.Descriptor);

        // Assert
        JsonNode.Parse(json)!.AsObject()["not_a_real_field"]!.GetValue<int>().ShouldBe(1);
    }

    [Fact]
    public void Null_request_type_skips_validation()
    {
        // Arrange — without a descriptor, the prior permissive behaviour is preserved.
        var entry = ConventionEntry();
        var selection = new ResolvedSelection("foo", "foo",
            new Dictionary<string, JsonNode?> { ["totallyUnknown"] = JsonValue.Create(1) },
            []);

        // Act
        var json = _translator.Translate(selection, entry, _defaults, requestType: null);

        // Assert
        JsonNode.Parse(json)!.AsObject()["totally_unknown"]!.GetValue<int>().ShouldBe(1);
    }

    private static MappingEntry ConventionEntry() => new()
    {
        GraphqlField = "foo",
        OperationType = GraphQLOperationType.Query,
        Service = "s",
        Method = "M"
    };
}
