using System.Text.Json.Nodes;
using Gql2Grpc.GraphQL;

namespace Gql2Grpc.Tests.Unit.GraphQL;

public sealed class SelectionResolverTests
{
    [Fact]
    public void Resolves_alias_as_response_key()
    {
        var (resolver, op) = Prepare("query { resp: emptyCall { id } }");
        var selections = resolver.Resolve(op.SelectionSet);

        selections.Count.ShouldBe(1);
        selections[0].ResponseKey.ShouldBe("resp");
        selections[0].Name.ShouldBe("emptyCall");
    }

    [Fact]
    public void Inlines_fragment_spreads()
    {
        var (resolver, op) = Prepare(@"
query { foo { ...F } }
fragment F on Foo { id name }");
        var selections = resolver.Resolve(op.SelectionSet);
        selections[0].Children.Select(c => c.Name).ShouldBe(["id", "name"]);
    }

    [Fact]
    public void Inlines_inline_fragments()
    {
        var (resolver, op) = Prepare("query { foo { ... on Foo { id } } }");
        var selections = resolver.Resolve(op.SelectionSet);
        selections[0].Children.Select(c => c.Name).ShouldBe(["id"]);
    }

    [Fact]
    public void Skip_directive_removes_field_when_variable_true()
    {
        var doc = GraphQLDocumentParser.Parse("query($skip: Boolean!) { a @skip(if: $skip) b }");
        var op = doc.SelectOperation(null);
        var vars = new Dictionary<string, JsonNode?> { ["skip"] = JsonValue.Create(true) };
        var resolver = new SelectionResolver(doc.Fragments, vars);

        var selections = resolver.Resolve(op.SelectionSet);
        selections.Select(s => s.Name).ShouldBe(["b"]);
    }

    [Fact]
    public void Include_directive_keeps_field_when_variable_true()
    {
        var doc = GraphQLDocumentParser.Parse("query($inc: Boolean!) { a @include(if: $inc) }");
        var op = doc.SelectOperation(null);
        var vars = new Dictionary<string, JsonNode?> { ["inc"] = JsonValue.Create(true) };
        var resolver = new SelectionResolver(doc.Fragments, vars);

        var selections = resolver.Resolve(op.SelectionSet);
        selections.Select(s => s.Name).ShouldBe(["a"]);
    }

    [Fact]
    public void Alias_collision_with_different_fields_throws()
    {
        var (resolver, op) = Prepare("query { a: foo a: bar }");
        Should.Throw<ArgumentException>(() => resolver.Resolve(op.SelectionSet));
    }

    private static (SelectionResolver Resolver, GraphQLOperation Op) Prepare(string source)
    {
        var doc = GraphQLDocumentParser.Parse(source);
        var op = doc.SelectOperation(null);
        var resolver = new SelectionResolver(doc.Fragments, new Dictionary<string, JsonNode?>());
        return (resolver, op);
    }
}
