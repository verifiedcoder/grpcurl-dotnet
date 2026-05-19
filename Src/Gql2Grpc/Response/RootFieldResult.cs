using Gql2Grpc.GraphQL;
using System.Text.Json.Nodes;

namespace Gql2Grpc.Response;

/// <summary>Per-root-field execution outcome, used to build the envelope.</summary>
/// <param name="ResponseKey">Output key (alias if supplied, otherwise the field name).</param>
/// <param name="Data">The projected data value, or <c>null</c> when the field failed.</param>
/// <param name="Errors">Field-level errors emitted by execution (might be empty).</param>
/// <param name="Failed">When <c>true</c>, the field's data was not produced; the envelope renders <c>data[key]: null</c>.</param>
public sealed record RootFieldResult(
    string ResponseKey,
    JsonNode? Data,
    IReadOnlyList<GraphQLError> Errors,
    bool Failed);