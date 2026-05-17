using Gql2Grpc.GraphQL;

namespace Gql2Grpc.Tests.Unit.GraphQL;

public sealed class GraphQLDocumentParserTests
{
    [Fact]
    public void Parses_single_query_operation()
    {
        var doc = GraphQLDocumentParser.Parse("query { emptyCall { x } }");

        doc.Operations.Count.ShouldBe(1);
        doc.Operations[0].OperationType.ShouldBe(GraphQLOperationType.Query);
        doc.Operations[0].Name.ShouldBeNull();
        doc.Fragments.Count.ShouldBe(0);
    }

    [Fact]
    public void Parses_mutation_and_subscription_operations()
    {
        var doc = GraphQLDocumentParser.Parse(@"
mutation CreateX { createResponse(input: {}) { id } }
subscription S { responseEvents { id } }");

        doc.Operations.Count.ShouldBe(2);
        doc.Operations.ShouldContain(o => o.OperationType == GraphQLOperationType.Mutation);
        doc.Operations.ShouldContain(o => o.OperationType == GraphQLOperationType.Subscription);
    }

    [Fact]
    public void Captures_fragment_definitions()
    {
        var doc = GraphQLDocumentParser.Parse(@"
query { thing { ...F } }
fragment F on Thing { id }");

        doc.Fragments.Count.ShouldBe(1);
        doc.Fragments.ShouldContainKey("F");
    }

    [Fact]
    public void SelectOperation_requires_name_when_multiple_exist()
    {
        var doc = GraphQLDocumentParser.Parse(@"
query A { x }
query B { y }");

        Should.Throw<ArgumentException>(() => doc.SelectOperation(null));
        doc.SelectOperation("A").Name.ShouldBe("A");
    }

    [Fact]
    public void Throws_on_empty_document()
    {
        Should.Throw<ArgumentException>(() => GraphQLDocumentParser.Parse(""));
    }

    [Fact]
    public void Throws_on_document_with_only_fragments()
    {
        Should.Throw<ArgumentException>(() =>
            GraphQLDocumentParser.Parse("fragment X on T { id }"));
    }
}
