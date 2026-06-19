using GrpCurl.Net.Studio.ViewModels.Models.GraphQl;

namespace GrpCurl.Net.Studio.ViewModels.Services;

/// <summary>
///     The single Studio seam over the <c>Gql2Grpc</c> bridge (SPEC-015 §2, GQL-007/008). It reuses the
///     bridge's parse / resolve / translate / execute pipeline verbatim — Studio never shells out to the
///     CLI and never forks the engine, so any behavioural difference from <c>gql2grpc</c> for the same
///     inputs is a bug. ViewModels depend only on this interface and the Studio model types it exposes;
///     the bridge's internal types never leak past the implementation.
/// </summary>
public interface IGraphQlService
{
    /// <summary>
    ///     Parses a GraphQL document (no network): enumerates its operations for the picker (GQL-012)
    ///     and reports any syntax problem so the editor can squiggle it and block Execute (GQL-011).
    /// </summary>
    GraphQlParseResult Parse(string document);

    /// <summary>
    ///     Executes one query/mutation operation against the request's connection and returns the GraphQL
    ///     envelope (GQL-021/022). Variables are coerced first; a coercion / parse / unresolved-mapping
    ///     failure surfaces as a configuration error with no RPC attempted (AC-5). Subscriptions are
    ///     handled by a later epic (E4.3) and are rejected here. When <paramref name="progress" /> is
    ///     supplied, each root field reports its parallel-scheduler transitions (GQL-024 / AC-6); the sink
    ///     may be called from worker threads, so the caller marshals to the UI thread.
    /// </summary>
    Task<GraphQlExecutionResult> ExecuteAsync(
        GraphQlExecutionRequest request,
        IProgress<GraphQlFieldProgress>? progress,
        CancellationToken cancellationToken);

    /// <summary>
    ///     Executes a subscription (server-streaming) operation, yielding one complete GraphQL envelope per
    ///     streamed message (GQL-060/061) as it arrives. Setup failures (parse / coercion / unresolved
    ///     mapping) and per-message errors are yielded as error-envelope lines. Cancellation stops the
    ///     stream after the envelopes already produced (AC-3). Each yielded string is one NDJSON envelope.
    /// </summary>
    IAsyncEnumerable<string> StreamAsync(GraphQlExecutionRequest request, CancellationToken cancellationToken);

    /// <summary>
    ///     Derives the GraphQL schema for the connection by answering <c>__schema</c> locally from the
    ///     descriptor set (GQL-075) — no business RPC. Reflects the mapping's <c>defaults.introspection</c>
    ///     (GQL-076). A descriptor-load failure surfaces on <see cref="GraphQlSchemaResult.Error" />.
    /// </summary>
    Task<GraphQlSchemaResult> IntrospectAsync(GraphQlExecutionRequest request, CancellationToken cancellationToken);

    /// <summary>
    ///     Resolves each root field of the document to its target (GQL-040..043) using the mapping +
    ///     default-service — purely (no RPC). Drives the live resolution preview: explicit-vs-convention
    ///     source, the resolved kind, the convention derivation, and unresolvable-field remedies.
    /// </summary>
    Task<GraphQlResolutionResult> ResolveAsync(GraphQlExecutionRequest request, CancellationToken cancellationToken);

    /// <summary>
    ///     Validates an inline mapping document against the loader's schema (GQL-045) — version, defaults,
    ///     and operation-entry shape — with no network. Returns the problems to surface in the mapping
    ///     editor; an empty list means the mapping loads cleanly.
    /// </summary>
    IReadOnlyList<GraphQlProblem> ValidateMapping(string mappingText);

    /// <summary>
    ///     Produces the exact pre-flight gRPC request JSON for each root field (GQL-050) — variables coerced,
    ///     mapping rules applied — with no RPC, and reports any argument the translator would silently drop
    ///     because it matches no request field (the Finding-4 guard, GQL-047). Needs the descriptor set.
    /// </summary>
    Task<GraphQlTranslationResult> TranslateAsync(GraphQlExecutionRequest request, CancellationToken cancellationToken);
}
