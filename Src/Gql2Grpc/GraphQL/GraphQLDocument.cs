using GraphQLParser.AST;

namespace Gql2Grpc.GraphQL;

/// <summary>
///     A parsed GraphQL document: the set of operations it declares and the fragment definitions
///     available for resolution. Produced by <see cref="GraphQLDocumentParser" />.
/// </summary>
/// <remarks>Constructs a document with the given operations and fragment definitions.</remarks>
// ReSharper disable once InconsistentNaming
public sealed class GraphQLDocument(
    IReadOnlyList<GraphQLOperation> operations,
    IReadOnlyDictionary<string, GraphQLFragmentDefinition> fragments)
{
    /// <summary>All executable operations declared by the document, in source order.</summary>
    public IReadOnlyList<GraphQLOperation> Operations { get; } = operations;

    /// <summary>Fragment definitions, keyed by name, available to <see cref="SelectionResolver" />.</summary>
    public IReadOnlyDictionary<string, GraphQLFragmentDefinition> Fragments { get; } = fragments;

    /// <summary>
    ///     Selects the operation to execute. When the document has a single operation, the name is optional.
    ///     When it has multiple operations, <paramref name="operationName" /> is required and must match.
    /// </summary>
    public GraphQLOperation SelectOperation(string? operationName)
    {
        if (Operations.Count == 0)
        {
            throw new ArgumentException("Document contains no executable operations.");
        }

        if (operationName is null)
        {
            if (Operations.Count == 1)
            {
                return Operations[0];
            }

            throw new ArgumentException(
                $"Document contains {Operations.Count} operations; --operation <name> is required to choose one.");
        }

        var match = Operations.FirstOrDefault(o => string.Equals(o.Name, operationName, StringComparison.Ordinal));

        if (match is not null)
        {
            return match;
        }

        var names = string.Join(", ", Operations.Select(o => o.Name ?? "(anonymous)"));

        throw new ArgumentException($"No operation named '{operationName}' in document. Available: {names}.");
    }
}