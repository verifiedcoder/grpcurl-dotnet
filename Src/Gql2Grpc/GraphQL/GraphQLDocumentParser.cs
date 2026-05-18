using GraphQLParser;
using GraphQLParser.AST;

namespace Gql2Grpc.GraphQL;

/// <summary>
/// Wraps <see cref="Parser.Parse(ROM, ParserOptions)"/> to produce a <see cref="GraphQLDocument"/>
/// with operations classified by type and a fragment lookup ready for resolution.
/// </summary>
public static class GraphQLDocumentParser
{
    /// <summary>
    /// Parses a GraphQL document string into a <see cref="GraphQLDocument"/>.
    /// </summary>
    /// <param name="source">A GraphQL document containing one or more operations and zero or more fragments.</param>
    /// <exception cref="ArgumentException">Thrown if the document is empty, contains no operations, or has an unknown operation type.</exception>
    public static GraphQLDocument Parse(string source)
    {
        if (string.IsNullOrWhiteSpace(source))
        {
            throw new ArgumentException("GraphQL document is empty.", nameof(source));
        }

        var document = Parser.Parse(source);

        var operations = new List<GraphQLOperation>();
        var fragments = new Dictionary<string, GraphQLFragmentDefinition>(StringComparer.Ordinal);

        foreach (var definition in document.Definitions)
        {
            switch (definition)
            {
                case GraphQLOperationDefinition op:
                    operations.Add(new GraphQLOperation(
                        op.Name?.StringValue,
                        MapOperationType(op.Operation),
                        op.Variables?.Items ?? (IReadOnlyList<GraphQLVariableDefinition>)[],
                        op.SelectionSet));
                    break;

                case GraphQLFragmentDefinition frag:
                    fragments[frag.FragmentName.Name.StringValue] = frag;
                    break;
            }
        }

        if (operations.Count == 0)
        {
            // Document with only fragments is not executable.
            throw new ArgumentException("Document contains no executable operations.");
        }

        return new GraphQLDocument(operations, fragments);
    }

    private static GraphQLOperationType MapOperationType(OperationType type) => type switch
    {
        OperationType.Query => GraphQLOperationType.Query,
        OperationType.Mutation => GraphQLOperationType.Mutation,
        OperationType.Subscription => GraphQLOperationType.Subscription,
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, $"Unknown operation type '{type}'.")
    };
}
