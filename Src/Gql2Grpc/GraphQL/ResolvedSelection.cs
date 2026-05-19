using System.Text.Json.Nodes;

namespace Gql2Grpc.GraphQL;

/// <summary>
///     A post-resolution GraphQL selection node. Fragments are inlined, directives (@include/@skip) are
///     already evaluated, variables are substituted into argument values, and aliases are captured as
///     <see cref="ResponseKey" />. Downstream layers (translator, projector) never see raw AST.
/// </summary>
public sealed record ResolvedSelection(
    string ResponseKey,
    string Name,
    IReadOnlyDictionary<string, JsonNode?> Arguments,
    IReadOnlyList<ResolvedSelection> Children);