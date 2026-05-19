using Gql2Grpc.GraphQL;

namespace Gql2Grpc.Configuration;

/// <summary>Single mapping rule — binds one GraphQL root field to a gRPC service/method pair.</summary>
public sealed record MappingEntry
{
    /// <summary>Name of the GraphQL field this entry targets (response key, not alias).</summary>
    public required string GraphqlField { get; init; }

    /// <summary>Whether the field appears under <c>query</c>, <c>mutation</c>, or <c>subscription</c>.</summary>
    public required GraphQLOperationType OperationType { get; init; }

    /// <summary>Fully-qualified gRPC service. When omitted, falls back to <see cref="MappingDefaults.Service" />.</summary>
    public string? Service { get; init; }

    /// <summary>Method name on the resolved gRPC service.</summary>
    public required string Method { get; init; }

    /// <summary>Whether the gRPC method is unary or server-streaming. Bidirectional/client-streaming are not supported.</summary>
    public MethodKind Kind { get; init; } = MethodKind.Unary;

    /// <summary>Per-argument rewrite rules (rename, literal substitution, skip, nested-path placement).</summary>
    public IReadOnlyDictionary<string, ArgumentRule> Arguments { get; init; } =
        new Dictionary<string, ArgumentRule>(StringComparer.Ordinal);

    /// <summary>Optional response-shape projection (e.g., unwrap a single field of the gRPC response).</summary>
    public ResponseShaping? Response { get; init; }

    /// <summary>
    ///     Optional FieldMask target request path, populated from the pseudo argument
    ///     <c>$selection: { fieldMask: &lt;target&gt; }</c>. When set, the translator
    ///     produces a <c>google.protobuf.FieldMask</c> value at the given request path
    ///     derived from the resolved selection tree.
    /// </summary>
    public string? SelectionFieldMaskPath { get; init; }
}