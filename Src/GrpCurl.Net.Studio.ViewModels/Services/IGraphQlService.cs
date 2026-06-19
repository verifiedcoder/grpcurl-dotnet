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
    ///     handled by a later epic (E4.3) and are rejected here.
    /// </summary>
    Task<GraphQlExecutionResult> ExecuteAsync(GraphQlExecutionRequest request, CancellationToken cancellationToken);
}
