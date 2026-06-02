using Gql2Grpc.GraphQL;

namespace Gql2Grpc.Configuration;

/// <summary>
///     Resolves a GraphQL root field (name + operation type) to a <see cref="MappingEntry" />.
///     Explicit entries in the config take precedence; when no entry matches, a convention-based
///     entry is synthesised from the supplied <see cref="MappingDefaults" /> and the CLI default service.
/// </summary>
/// <remarks>
///     Constructs a resolver against a loaded <paramref name="config" />. The optional
///     <paramref name="cliDefaultService" /> mirrors the CLI's <c>--default-service</c> flag and
///     supplies a fully-qualified gRPC service for entries (or convention fallbacks) that omit one.
/// </remarks>
public sealed class MappingResolver(MappingConfig config, string? cliDefaultService)
{
    private readonly Dictionary<(string, GraphQLOperationType), MappingEntry> _explicitLookup = BuildLookup(config.Operations);

    /// <summary>The underlying configuration this resolver was constructed from.</summary>
    public MappingConfig Config { get; } = config;

    /// <summary>
    ///     Resolves a GraphQL root field to a concrete <see cref="MappingEntry" />. Throws
    ///     <see cref="InvalidOperationException" /> when no explicit entry matches and no default
    ///     service is configured.
    /// </summary>
    public MappingEntry Resolve(string graphqlField, GraphQLOperationType operationType)
        => _explicitLookup.TryGetValue((graphqlField, operationType), out var explicitEntry)
            ? WithResolvedService(explicitEntry)
            : SynthesiseConvention(graphqlField, operationType);

    private MappingEntry WithResolvedService(MappingEntry entry)
    {
        if (entry.Service is not null)
        {
            return entry;
        }

        var service = cliDefaultService ?? Config.Defaults.Service
            ?? throw new InvalidOperationException(
                $"Operations entry '{entry.GraphqlField}' has no 'service' and no defaults.service / --default-service is set.");

        return entry with { Service = service };
    }

    private MappingEntry SynthesiseConvention(string graphqlField, GraphQLOperationType operationType)
    {
        var service = cliDefaultService ?? Config.Defaults.Service
            ?? throw new InvalidOperationException(
                $"No mapping for GraphQL field '{graphqlField}' and no default service. Pass --default-service or add an entry to --mapping.");

        var method = Config.Defaults.Convention.PascalCaseFieldNames
            ? ConventionDefaults.ToPascalCase(graphqlField)
            : graphqlField;

        if (!string.IsNullOrEmpty(Config.Defaults.Convention.ListMethodPrefix))
        {
            method = Config.Defaults.Convention.ListMethodPrefix + method;
        }

        var kind = operationType == GraphQLOperationType.Subscription
            ? MethodKind.ServerStreaming
            : MethodKind.Unary;

        return new MappingEntry
        {
            GraphqlField = graphqlField,
            OperationType = operationType,
            Service = service,
            Method = method,
            Kind = kind
        };
    }

    private static Dictionary<(string, GraphQLOperationType), MappingEntry> BuildLookup(
        IReadOnlyList<MappingEntry> operations)
    {
        var dict = new Dictionary<(string, GraphQLOperationType), MappingEntry>(operations.Count);

        foreach (var entry in operations)
        {
            dict[(entry.GraphqlField, entry.OperationType)] = entry;
        }

        return dict;
    }
}