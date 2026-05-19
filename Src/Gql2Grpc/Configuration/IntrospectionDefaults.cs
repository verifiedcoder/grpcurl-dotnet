namespace Gql2Grpc.Configuration;

/// <summary>Defaults that shape the responses returned by GraphQL introspection queries.</summary>
public sealed record IntrospectionDefaults
{
    /// <summary>Optional name reported as the <c>__schema.queryType.name</c>.</summary>
    public string? SchemaName { get; init; }

    /// <summary>Protobuf-message-name → GraphQL-type-name overrides used when projecting introspection.</summary>
    public IReadOnlyDictionary<string, string> TypeOverrides { get; init; } =
        new Dictionary<string, string>(StringComparer.Ordinal);
}