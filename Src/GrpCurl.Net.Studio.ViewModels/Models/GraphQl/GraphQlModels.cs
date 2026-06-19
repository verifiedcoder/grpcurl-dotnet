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
///     A declared operation variable (GQL-018): its name, printed GraphQL type (e.g. <c>Int!</c>,
///     <c>[String!]</c>), and whether it is required (a non-null type with no default).
/// </summary>
public sealed record GraphQlVariableInfo(string Name, string Type, bool Required);

/// <summary>
///     One operation discovered while parsing a document — drives the operation picker (GQL-012) and the
///     quick-vars grid (GQL-018). The name is null for an anonymous operation.
/// </summary>
public sealed record GraphQlOperationInfo(string? Name, GraphQlOperationKind Kind)
{
    /// <summary>Picker label: the operation name, or a placeholder for the anonymous operation.</summary>
    public string DisplayName => Name ?? "(anonymous)";

    /// <summary>The operation's declared variables (drives the quick-vars grid + unbound/undeclared warnings).</summary>
    public IReadOnlyList<GraphQlVariableInfo> Variables { get; init; } = [];
}

/// <summary>A problem surfaced in the Problems strip / editor squiggle. Line/column are 1-based when known.</summary>
public sealed record GraphQlProblem(string Message, GraphQlProblemKind Kind, int? Line = null, int? Column = null);

/// <summary>Lifecycle state of one root field while a multi-field document executes (GQL-024).</summary>
public enum GraphQlFieldState
{
    Queued,
    InFlight,
    Done,
    Failed
}

/// <summary>
///     A per-root-field progress notification raised as the document executes through the bounded-4
///     parallel scheduler (GQL-024 / AC-6). <see cref="ElapsedMs" /> is populated on the terminal states.
/// </summary>
public sealed record GraphQlFieldProgress(int Index, string ResponseKey, GraphQlFieldState State, double? ElapsedMs = null);

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
///     A completed GraphQL execution captured for history (GQL-027 / FR-120). Carries the redaction-ready
///     request snapshot (document + headers, secrets redacted by the recorder) and the outcome. The
///     response envelope is stored only when opt-in capture is enabled.
/// </summary>
public sealed record GraphQlHistoryContext(
    SavedConnection Connection,
    string OperationLabel,
    string Document,
    IReadOnlyList<HeaderEntry> Headers,
    string? Deadline,
    bool EmitDefaults,
    bool AllowUnknownFields,
    string? EnvironmentName,
    bool Ok,
    string Status,
    string Category,
    string? ErrorMessage,
    long DurationMs,
    string? ResponseEnvelope);

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
