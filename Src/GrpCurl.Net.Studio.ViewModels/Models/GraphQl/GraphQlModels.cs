using GrpCurl.Net.Studio.ViewModels.Models.Connections;

namespace GrpCurl.Net.Studio.ViewModels.Models.GraphQl;

/// <summary>The three GraphQL operation kinds (mirrors the bridge's operation type, UI-side).</summary>
public enum GraphQlOperationKind
{
    Query,
    Mutation,
    Subscription
}

/// <summary>Verbose-pane level (GQL-029): off, resolved-mapping (-v), or +request-JSON (-vv).</summary>
public enum GraphQlVerbosity
{
    Off,
    Verbose,
    VeryVerbose
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

    /// <summary>Top-level root-field count — used for the subscription "never parallelised" pre-flight (GQL-064).</summary>
    public int RootFieldCount { get; init; }
}

/// <summary>A problem surfaced in the Problems strip / editor squiggle. Line/column are 1-based when known.</summary>
public sealed record GraphQlProblem(string Message, GraphQlProblemKind Kind, int? Line = null, int? Column = null);

/// <summary>
///     How a response <c>errors[]</c> entry should be presented (GQL-073): an upstream gRPC failure is
///     visually distinct from a usage/configuration error. Classification is by error <em>kind</em>
///     (does it carry an upstream gRPC status) rather than by trusting <c>extensions.code</c>.
/// </summary>
public enum GraphQlErrorClass
{
    Configuration,
    Upstream,
    Unknown
}

/// <summary>
///     One structured entry from a response envelope's <c>errors[]</c> (GQL-070): its message, the
///     <c>path</c> breadcrumb, and the relevant <c>extensions</c> (code, and the upstream gRPC status when
///     present).
/// </summary>
public sealed record GraphQlErrorInfo(
    string Message,
    IReadOnlyList<string> Path,
    string? Code,
    string? GrpcStatus,
    int? GrpcStatusCode,
    GraphQlErrorClass Class)
{
    public bool HasPath => Path.Count > 0;

    /// <summary>The path rendered as a breadcrumb (GQL-070).</summary>
    public string PathText => string.Join(" › ", Path);

    public bool IsUpstream => Class == GraphQlErrorClass.Upstream;

    /// <summary>A compact one-line summary of the extensions (code + upstream status when present).</summary>
    public string ExtensionsText
    {
        get
        {
            var parts = new List<string>();
            if (!string.IsNullOrEmpty(Code))
            {
                parts.Add(Code);
            }

            if (GrpcStatus is not null)
            {
                parts.Add(GrpcStatusCode is { } code ? $"{GrpcStatus} ({code})" : GrpcStatus);
            }

            return string.Join(" · ", parts);
        }
    }
}

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
    bool Raw,
    GraphQlVerbosity Verbosity = GraphQlVerbosity.Off,
    string? MappingText = null);

/// <summary>How a root field's target was resolved (GQL-040/041): an explicit mapping entry, convention fallback, or not at all.</summary>
public enum GraphQlResolutionSource
{
    ExplicitEntry,
    Convention,
    Unresolved
}

/// <summary>
///     The resolved target of one root field (GQL-040): its <c>service/Method</c>, kind (unary /
///     serverStreaming), and whether it came from an explicit entry or convention. Unresolvable fields
///     carry the bridge's remedy message (GQL-042) — computed with no RPC.
/// </summary>
public sealed record GraphQlFieldResolution(
    string FieldName,
    bool Resolved,
    string? Service,
    string? Method,
    string? Kind,
    GraphQlResolutionSource Source,
    string? Derivation,
    string? Error)
{
    public string Target => Resolved ? $"{Service}/{Method}" : "(unresolved)";

    public bool IsExplicit => Source == GraphQlResolutionSource.ExplicitEntry;

    public bool IsConvention => Source == GraphQlResolutionSource.Convention;

    public bool IsUnresolved => Source == GraphQlResolutionSource.Unresolved;

    public bool HasDerivation => !string.IsNullOrEmpty(Derivation);
}

/// <summary>
///     The live resolution preview for the current document (GQL-040..043): one entry per root field, plus
///     whether the tab's default-service overrode the mapping's <c>defaults.service</c> (GQL-041).
/// </summary>
public sealed record GraphQlResolutionResult(
    IReadOnlyList<GraphQlFieldResolution> Fields,
    bool DefaultServiceOverridden,
    string? OverriddenService);

/// <summary>One field / enum value / union member of a derived schema type (GQL-075).</summary>
public sealed record GraphQlSchemaMember(string Name, string? TypeName);

/// <summary>One type in the derived GraphQL schema (GQL-075): its name, kind, and members.</summary>
public sealed record GraphQlSchemaType(string Name, string Kind, IReadOnlyList<GraphQlSchemaMember> Members)
{
    public bool HasMembers => Members.Count > 0;
}

/// <summary>
///     The locally-derived GraphQL schema (GQL-075/076): the navigable type tree plus the raw introspection
///     JSON (for "copy introspection JSON"). Produced without any RPC. <see cref="Error" /> is set when the
///     descriptor set could not be loaded.
/// </summary>
public sealed record GraphQlSchemaResult(
    bool Ok,
    string SchemaName,
    IReadOnlyList<GraphQlSchemaType> Types,
    string? Json,
    GraphQlProblem? Error);

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
    /// <summary>Captured verbose-pane lines (GQL-029): per-field resolved mapping, and request JSON at -vv.</summary>
    public IReadOnlyList<string> VerboseLog { get; init; } = [];

    /// <summary>The structured <c>errors[]</c> from the envelope (GQL-070); empty when the call fully succeeded.</summary>
    public IReadOnlyList<GraphQlErrorInfo> Errors { get; init; } = [];

    /// <summary>A configuration error short-circuited execution before any RPC (AC-5 / GQL-073).</summary>
    public bool IsConfigurationError => ConfigurationErrors.Count > 0;
}
