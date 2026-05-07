using GraphQLParser.AST;

namespace Gql2Grpc.GraphQL;

/// <summary>
/// A single GraphQL operation (query, mutation, or subscription) from a parsed document.
/// The raw AST nodes are retained because <see cref="SelectionResolver"/> needs to walk them with
/// the fragment table plus coerced variables to produce <see cref="ResolvedSelection"/> values.
/// </summary>
public sealed record GraphQLOperation(
    string? Name,
    GraphQLOperationType OperationType,
    IReadOnlyList<GraphQLVariableDefinition> VariableDefinitions,
    GraphQLSelectionSet SelectionSet);
