using GrpCurl.Net.Studio.ViewModels.Models.Connections;

namespace GrpCurl.Net.Studio.ViewModels.Models.GraphQl;

/// <summary>The three GraphQL operation kinds (mirrors the bridge's operation type, UI-side).</summary>
public enum GraphQlOperationKind
{
    Query,
    Mutation,
    Subscription
}

/// <summary>
///     How a problem should be presented (SPEC-015 §6): a usage/configuration problem (unresolvable
///     field, mapping-load/coercion/parse failure) is visually distinct from an upstream RPC error.
/// </summary>
public enum GraphQlProblemKind
{
    /// <summary>A GraphQL document syntax error.</summary>
    Syntax,

    /// <summary>A variables problem (JSON syntax or per-variable coercion failure) — pre-RPC (AC-5).</summary>
    Variables,

    /// <summary>A mapping/resolution configuration error (e.g. no mapping for a field and no default service).</summary>
    Configuration
}

/// <summary>
///     One operation discovered while parsing a document — drives the operation picker (GQL-012). The
///     name is null for an anonymous operation.
/// </summary>
public sealed record GraphQlOperationInfo(string? Name, GraphQlOperationKind Kind)
{
    /// <summary>Picker label: the operation name, or a placeholder for the anonymous operation.</summary>
    public string DisplayName => Name ?? "(anonymous)";
}

/// <summary>A problem surfaced in the Problems strip / editor squiggle. Line/column are 1-based when known.</summary>
public sealed record GraphQlProblem(string Message, GraphQlProblemKind Kind, int? Line = null, int? Column = null);

/// <summary>Outcome of parsing a document (no network): the operations found and any syntax problems.</summary>
public sealed record GraphQlParseResult(
    IReadOnlyList<GraphQlOperationInfo> Operations,
    IReadOnlyList<GraphQlProblem> Problems)
{
    /// <summary>The document parsed cleanly (Execute may proceed once an operation is chosen).</summary>
    public bool Ok => Problems.Count == 0;
}

/// <summary>
///     A request to execute one GraphQL operation against a connection. Carries only Studio/primitive
///     types so the ViewModels never see bridge internals (GQL-008).
/// </summary>
public sealed record GraphQlExecutionRequest(
    SavedConnection Connection,
    string Document,
    string? OperationName,
    string? VariablesJson,
    string? DefaultService,
    string? MappingPath,
    IReadOnlyList<HeaderEntry> Headers,
    string? Deadline,
    bool EmitDefaults,
    bool AllowUnknownFields,
    bool StrictSelection,
    bool Introspection,
    bool Raw);

/// <summary>
///     Outcome of executing a GraphQL operation. <see cref="ConfigurationErrors" /> are pre-RPC
///     usage/configuration failures (parse, variable coercion, unresolved mapping) and, when present,
///     mean no RPC was attempted and <see cref="EnvelopeJson" /> is null. Otherwise
///     <see cref="EnvelopeJson" /> is the pretty-printed GraphQL envelope (<c>data</c> + <c>errors</c>).
/// </summary>
public sealed record GraphQlExecutionResult(
    bool Ok,
    string? EnvelopeJson,
    IReadOnlyList<GraphQlProblem> ConfigurationErrors)
{
    /// <summary>A configuration error short-circuited execution before any RPC (AC-5 / GQL-073).</summary>
    public bool IsConfigurationError => ConfigurationErrors.Count > 0;
}
