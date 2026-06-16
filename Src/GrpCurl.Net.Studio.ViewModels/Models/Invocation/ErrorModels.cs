namespace GrpCurl.Net.Studio.ViewModels.Models.Invocation;

/// <summary>
///     UI-free taxonomy of a failed invocation, mirroring Core's <c>ErrorCategory</c> so the
///     view-model layer never references the (internal) Core enum directly.
/// </summary>
public enum ErrorCategoryKind
{
    Usage,
    Schema,
    Network,
    Timeout,
    Rpc,
    Cancelled,
    Internal
}

/// <summary>
///     FR-091 colour grouping: the five severity buckets the error pill is keyed on. The pill text
///     is always the gRPC status <em>name</em> (a11y); only the colour varies by severity.
/// </summary>
public enum StatusSeverity
{
    Ok,
    Cancelled,
    Transient,
    Caller,
    Server
}

/// <summary>Maps a gRPC status code / error category to one of the five FR-091 severity groups.</summary>
public static class StatusSeverityMap
{
    public static StatusSeverity FromCode(int code) => code switch
    {
        0 => StatusSeverity.Ok,
        1 => StatusSeverity.Cancelled,                       // CANCELLED
        4 or 8 or 10 or 14 => StatusSeverity.Transient,      // DEADLINE_EXCEEDED, RESOURCE_EXHAUSTED, ABORTED, UNAVAILABLE
        3 or 5 or 6 or 7 or 9 or 11 or 16 => StatusSeverity.Caller, // INVALID_ARGUMENT, NOT_FOUND, ALREADY_EXISTS, PERMISSION_DENIED, FAILED_PRECONDITION, OUT_OF_RANGE, UNAUTHENTICATED
        _ => StatusSeverity.Server                           // UNKNOWN, UNIMPLEMENTED, INTERNAL, DATA_LOSS, …
    };

    public static StatusSeverity FromCategory(ErrorCategoryKind category) => category switch
    {
        ErrorCategoryKind.Cancelled => StatusSeverity.Cancelled,
        ErrorCategoryKind.Network or ErrorCategoryKind.Timeout => StatusSeverity.Transient,
        ErrorCategoryKind.Usage or ErrorCategoryKind.Schema => StatusSeverity.Caller,
        _ => StatusSeverity.Server
    };
}

/// <summary>A remediation hint (FR-095); <see cref="SettingLink" /> optionally deep-links to a settings page.</summary>
public sealed record SuggestionModel(string Text, string? SettingLink = null)
{
    public bool HasSettingLink => SettingLink is not null;
}

/// <summary>The complete, UI-ready description of a failed invocation (FR-090..099). No Core/Avalonia types leak in.</summary>
public sealed record ErrorModel(
    ErrorCategoryKind Category,
    int StatusCode,
    string StatusName,
    StatusSeverity Severity,
    string Headline,
    string? Hint,
    string? Address,
    string? Method,
    IReadOnlyList<SuggestionModel> Suggestions,
    IReadOnlyList<ErrorDetailModel> Details,
    string JsonEnvelope);

// ── Rich google.rpc.Status detail hierarchy (FR-090) ─────────────────────────

/// <summary>Base type for a decoded <c>google.rpc.Status</c> detail; <see cref="Title" /> labels its panel.</summary>
public abstract record ErrorDetailModel
{
    public abstract string Title { get; }
}

public sealed record FieldViolation(string Field, string Description);

/// <summary>google.rpc.BadRequest — field-level validation failures.</summary>
public sealed record BadRequestDetail(IReadOnlyList<FieldViolation> Violations) : ErrorDetailModel
{
    public override string Title => "Bad request";
}

/// <summary>google.rpc.RetryInfo — the server-advised delay before retrying.</summary>
public sealed record RetryInfoDetail(TimeSpan Delay) : ErrorDetailModel
{
    public override string Title => "Retry info";
}

/// <summary>google.rpc.ErrorInfo — machine-readable reason/domain plus metadata.</summary>
public sealed record ErrorInfoDetail(string Reason, string Domain, IReadOnlyList<MetadataItem> Metadata) : ErrorDetailModel
{
    public override string Title => "Error info";
}

public sealed record QuotaViolation(string Subject, string Description);

/// <summary>google.rpc.QuotaFailure — quota/limit violations.</summary>
public sealed record QuotaFailureDetail(IReadOnlyList<QuotaViolation> Violations) : ErrorDetailModel
{
    public override string Title => "Quota failure";
}

public sealed record PreconditionViolation(string Type, string Subject, string Description);

/// <summary>google.rpc.PreconditionFailure — unmet preconditions (e.g. terms of service).</summary>
public sealed record PreconditionFailureDetail(IReadOnlyList<PreconditionViolation> Violations) : ErrorDetailModel
{
    public override string Title => "Precondition failure";
}

public sealed record HelpLink(string Description, string Url);

/// <summary>google.rpc.Help — links to documentation describing the error.</summary>
public sealed record HelpDetail(IReadOnlyList<HelpLink> Links) : ErrorDetailModel
{
    public override string Title => "Help";
}

/// <summary>google.rpc.LocalizedMessage — a localized, user-facing message.</summary>
public sealed record LocalizedMessageDetail(string Locale, string Message) : ErrorDetailModel
{
    public override string Title => "Localized message";
}

/// <summary>
///     Any other / unknown detail (google.rpc.DebugInfo, RequestInfo, ResourceInfo, or an unrecognised
///     type URL) rendered as its type URL plus formatted JSON.
/// </summary>
public sealed record GenericDetail(string TypeUrl, string Json) : ErrorDetailModel
{
    public override string Title => ShortName(TypeUrl);

    private static string ShortName(string typeUrl)
    {
        // type URLs look like "type.googleapis.com/google.rpc.DebugInfo"; reduce to the leaf "DebugInfo".
        var name = typeUrl;
        var slash = name.LastIndexOf('/');
        if (slash >= 0 && slash < name.Length - 1)
        {
            name = name[(slash + 1)..];
        }

        var dot = name.LastIndexOf('.');
        return dot >= 0 && dot < name.Length - 1 ? name[(dot + 1)..] : name;
    }
}
