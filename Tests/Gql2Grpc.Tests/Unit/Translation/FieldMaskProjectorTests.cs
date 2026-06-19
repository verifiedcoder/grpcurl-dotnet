using Gql2Grpc.GraphQL;
using Gql2Grpc.Translation;
using System.Text.Json.Nodes;

namespace Gql2Grpc.Tests.Unit.Translation;

public sealed class FieldMaskProjectorTests
{
    [Fact]
    public void Leaf_selections_become_paths()
    {
        // Arrange
        var selections = new[]

        // Act
        {
            Leaf("id"),
            Leaf("firstName")
        };

        // Assert
        FieldMaskProjector.Build(selections).ShouldBe("id,first_name");
    }

    [Fact]
    public void Nested_selections_produce_dotted_paths()
    {
        // Arrange
        var selections = new[]

        // Act
        {
            new ResolvedSelection("payload", "payload", new Dictionary<string, JsonNode?>(),
            [
                Leaf("body"),
                Leaf("size")
            ])
        };

        // Assert
        FieldMaskProjector.Build(selections).ShouldBe("payload.body,payload.size");
    }

    [Fact]
    public void Empty_selection_produces_empty_string()
    {
        // Arrange

        // Assert

        // Act
        FieldMaskProjector.Build([]).ShouldBe(string.Empty);
    }

    private static ResolvedSelection Leaf(string name) =>
        new(name, name, new Dictionary<string, JsonNode?>(), []);
}
