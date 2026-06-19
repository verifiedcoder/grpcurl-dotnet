using Gql2Grpc.GraphQL;
using System.Text.Json.Nodes;

namespace Gql2Grpc.Tests.Unit.GraphQL;

public sealed class SelectionResolverTests
{
    [Fact]
    public void Resolves_alias_as_response_key()
    {
        // Arrange
        var (resolver, op) = Prepare("query { resp: emptyCall { id } }");

        // Act
        var selections = resolver.Resolve(op.SelectionSet);

        // Assert
        selections.Count.ShouldBe(1);
        selections[0].ResponseKey.ShouldBe("resp");
        selections[0].Name.ShouldBe("emptyCall");
    }

    [Fact]
    public void Inlines_fragment_spreads()
    {
        // Arrange
        var (resolver, op) = Prepare(@"
query { foo { ...F } }
fragment F on Foo { id name }");

        // Act
        var selections = resolver.Resolve(op.SelectionSet);

        // Assert
        selections[0].Children.Select(c => c.Name).ShouldBe(["id", "name"]);
    }

    [Fact]
    public void Inlines_inline_fragments()
    {
        // Arrange
        var (resolver, op) = Prepare("query { foo { ... on Foo { id } } }");

        // Act
        var selections = resolver.Resolve(op.SelectionSet);

        // Assert
        selections[0].Children.Select(c => c.Name).ShouldBe(["id"]);
    }

    [Fact]
    public void Skip_directive_removes_field_when_variable_true()
    {
        // Arrange
        var doc = GraphQLDocumentParser.Parse("query($skip: Boolean!) { a @skip(if: $skip) b }");
        var op = doc.SelectOperation(null);
        var vars = new Dictionary<string, JsonNode?> { ["skip"] = JsonValue.Create(true) };
        var resolver = new SelectionResolver(doc.Fragments, vars);

        // Act
        var selections = resolver.Resolve(op.SelectionSet);

        // Assert
        selections.Select(s => s.Name).ShouldBe(["b"]);
    }

    [Fact]
    public void Include_directive_keeps_field_when_variable_true()
    {
        // Arrange
        var doc = GraphQLDocumentParser.Parse("query($inc: Boolean!) { a @include(if: $inc) }");
        var op = doc.SelectOperation(null);
        var vars = new Dictionary<string, JsonNode?> { ["inc"] = JsonValue.Create(true) };
        var resolver = new SelectionResolver(doc.Fragments, vars);

        // Act
        var selections = resolver.Resolve(op.SelectionSet);

        // Assert
        selections.Select(s => s.Name).ShouldBe(["a"]);
    }

    [Fact]
    public void Alias_collision_with_different_fields_throws()
    {
        // Arrange

        // Act
        var (resolver, op) = Prepare("query { a: foo a: bar }");

        // Assert
        _ = Should.Throw<ArgumentException>(() => resolver.Resolve(op.SelectionSet));
    }

    private static (SelectionResolver Resolver, GraphQLOperation Op) Prepare(string source)
    {
        var doc = GraphQLDocumentParser.Parse(source);
        var op = doc.SelectOperation(null);
        var resolver = new SelectionResolver(doc.Fragments, new Dictionary<string, JsonNode?>());
        return (resolver, op);
    }
}
