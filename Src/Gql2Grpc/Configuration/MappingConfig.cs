namespace Gql2Grpc.Configuration;

/// <summary>
///     Top-level mapping configuration that drives <c>gql2grpc</c>'s GraphQL-to-gRPC
///     translation. Loaded from a YAML or JSON file via <see cref="MappingConfigLoader" />.
/// </summary>
public sealed record MappingConfig
{
    /// <summary>Schema version of the configuration file. Currently, always <c>1</c>.</summary>
    public int Version { get; init; } = 1;

    /// <summary>Defaults applied to every <see cref="MappingEntry" /> that does not override them.</summary>
    public MappingDefaults Defaults { get; init; } = new();

    /// <summary>Per-operation mapping rules — at least one must match each GraphQL root field.</summary>
    public IReadOnlyList<MappingEntry> Operations { get; init; } = [];

    /// <summary>An empty configuration with default values, used when no <c>--mapping</c> is supplied.</summary>
    public static MappingConfig Empty { get; } = new();
}