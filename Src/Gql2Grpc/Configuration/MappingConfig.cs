using Gql2Grpc.GraphQL;

namespace Gql2Grpc.Configuration;

/// <summary>
/// Top-level mapping configuration that drives <c>gql2grpc</c>'s GraphQL-to-gRPC
/// translation. Loaded from a YAML or JSON file via <see cref="MappingConfigLoader"/>.
/// </summary>
public sealed record MappingConfig
{
    /// <summary>Schema version of the configuration file. Currently always <c>1</c>.</summary>
    public int Version { get; init; } = 1;

    /// <summary>Defaults applied to every <see cref="MappingEntry"/> that does not override them.</summary>
    public MappingDefaults Defaults { get; init; } = new();

    /// <summary>Per-operation mapping rules — at least one must match each GraphQL root field.</summary>
    public IReadOnlyList<MappingEntry> Operations { get; init; } = [];

    /// <summary>An empty configuration with default values, used when no <c>--mapping</c> is supplied.</summary>
    public static MappingConfig Empty { get; } = new();
}

/// <summary>Defaults applied across every <see cref="MappingEntry"/> in a <see cref="MappingConfig"/>.</summary>
public sealed record MappingDefaults
{
    /// <summary>Fully-qualified gRPC service used by the convention-based fallback when no entry matches.</summary>
    public string? Service { get; init; }

    /// <summary>GraphQL-argument-name → gRPC-field-name aliases applied to every entry.</summary>
    public IReadOnlyDictionary<string, string> ArgumentAliases { get; init; } =
        new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>Conventions that derive method and field names when no explicit mapping is supplied.</summary>
    public MappingConvention Convention { get; init; } = new();

    /// <summary>Defaults for GraphQL introspection (<c>__schema</c>, <c>__type</c>, etc.).</summary>
    public IntrospectionDefaults Introspection { get; init; } = new();
}

/// <summary>
/// Naming conventions used by the convention-based fallback resolver. Applied when a GraphQL
/// field has no explicit <see cref="MappingEntry"/> and a <see cref="MappingDefaults.Service"/> is set.
/// </summary>
public sealed record MappingConvention
{
    /// <summary>String prefix used to identify list-method names (e.g. <c>list</c>, <c>get_</c>).</summary>
    public string ListMethodPrefix { get; init; } = string.Empty;

    /// <summary>When <c>true</c>, GraphQL field names are PascalCased before matching gRPC method names.</summary>
    public bool PascalCaseFieldNames { get; init; } = true;
}

/// <summary>Defaults that shape the responses returned by GraphQL introspection queries.</summary>
public sealed record IntrospectionDefaults
{
    /// <summary>Optional name reported as the <c>__schema.queryType.name</c>.</summary>
    public string? SchemaName { get; init; }

    /// <summary>Protobuf-message-name → GraphQL-type-name overrides used when projecting introspection.</summary>
    public IReadOnlyDictionary<string, string> TypeOverrides { get; init; } =
        new Dictionary<string, string>(StringComparer.Ordinal);
}

/// <summary>Single mapping rule — binds one GraphQL root field to a gRPC service/method pair.</summary>
public sealed record MappingEntry
{
    /// <summary>Name of the GraphQL field this entry targets (response key, not alias).</summary>
    public required string GraphqlField { get; init; }

    /// <summary>Whether the field appears under <c>query</c>, <c>mutation</c>, or <c>subscription</c>.</summary>
    public required GraphQLOperationType OperationType { get; init; }

    /// <summary>Fully-qualified gRPC service. When omitted, falls back to <see cref="MappingDefaults.Service"/>.</summary>
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
    /// Optional FieldMask target request path, populated from the pseudo argument
    /// <c>$selection: { fieldMask: &lt;target&gt; }</c>. When set, the translator
    /// produces a <c>google.protobuf.FieldMask</c> value at the given request path
    /// derived from the resolved selection tree.
    /// </summary>
    public string? SelectionFieldMaskPath { get; init; }
}

/// <summary>Streaming kind of a gRPC method that <c>gql2grpc</c> can route to.</summary>
public enum MethodKind
{
    /// <summary>Single request, single response.</summary>
    Unary,

    /// <summary>Single request, server-pushed stream of responses.</summary>
    ServerStreaming
}

/// <summary>Discriminated union of per-argument rewrite rules supplied by a <see cref="MappingEntry"/>.</summary>
public abstract record ArgumentRule
{
    /// <summary>Rename a GraphQL argument to a different gRPC field at the top of the request body.</summary>
    /// <param name="GrpcFieldName">Target gRPC field name.</param>
    public sealed record Rename(string GrpcFieldName) : ArgumentRule;

    /// <summary>Place a GraphQL argument's value at a nested gRPC request path (dot-separated).</summary>
    /// <param name="Path">Dot-separated nested field path inside the gRPC request message.</param>
    public sealed record PathRule(string Path) : ArgumentRule;

    /// <summary>Substitute a constant literal value for an argument, regardless of what the client supplied.</summary>
    /// <param name="Value">Literal JSON value (already serialized) inserted into the gRPC request.</param>
    public sealed record Literal(string Value) : ArgumentRule;

    /// <summary>Drop an argument entirely; never forward it to the gRPC request body.</summary>
    public sealed record SkipArgument : ArgumentRule;
}

/// <summary>Reshape rules applied to the gRPC response before projection into the GraphQL envelope.</summary>
public sealed record ResponseShaping
{
    /// <summary>Optional gRPC field name whose value replaces the entire response payload.</summary>
    public string? Unwrap { get; init; }
}
