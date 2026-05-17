using System.Text.Json.Nodes;
using Gql2Grpc.GraphQL;
using Gql2Grpc.Translation;

namespace Gql2Grpc.Tests.Unit.Translation;

public sealed class FieldMaskProjectorTests
{
    [Fact]
    public void Leaf_selections_become_paths()
    {
        var selections = new[]
        {
            Leaf("id"),
            Leaf("firstName")
        };

        FieldMaskProjector.Build(selections).ShouldBe("id,first_name");
    }

    [Fact]
    public void Nested_selections_produce_dotted_paths()
    {
        var selections = new[]
        {
            new ResolvedSelection("payload", "payload", new Dictionary<string, JsonNode?>(),
            [
                Leaf("body"),
                Leaf("size")
            ])
        };

        FieldMaskProjector.Build(selections).ShouldBe("payload.body,payload.size");
    }

    [Fact]
    public void Empty_selection_produces_empty_string()
    {
        FieldMaskProjector.Build(Array.Empty<ResolvedSelection>()).ShouldBe(string.Empty);
    }

    private static ResolvedSelection Leaf(string name) =>
        new(name, name, new Dictionary<string, JsonNode?>(), Array.Empty<ResolvedSelection>());
}
