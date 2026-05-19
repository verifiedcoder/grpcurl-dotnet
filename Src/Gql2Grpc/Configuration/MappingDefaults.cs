namespace Gql2Grpc.Configuration;

/// <summary>Defaults applied across every <see cref="MappingEntry" /> in a <see cref="MappingConfig" />.</summary>
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