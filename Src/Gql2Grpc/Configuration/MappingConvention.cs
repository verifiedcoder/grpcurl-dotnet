namespace Gql2Grpc.Configuration;

/// <summary>
///     Naming conventions used by the convention-based fallback resolver. Applied when a GraphQL
///     field has no explicit <see cref="MappingEntry" /> and a <see cref="MappingDefaults.Service" /> is set.
/// </summary>
public sealed record MappingConvention
{
    /// <summary>String prefix used to identify list-method names (e.g. <c>list</c>, <c>get_</c>).</summary>
    public string ListMethodPrefix { get; init; } = string.Empty;

    /// <summary>When <c>true</c>, GraphQL field names are PascalCased before matching gRPC method names.</summary>
    public bool PascalCaseFieldNames { get; init; } = true;
}