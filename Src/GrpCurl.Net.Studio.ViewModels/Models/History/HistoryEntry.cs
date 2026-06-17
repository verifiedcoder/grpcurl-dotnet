using System.Text.Json.Serialization;

namespace GrpCurl.Net.Studio.ViewModels.Models.History;

/// <summary>Which surface produced a history entry (SPEC-040 §5.1), serialized as <c>grpc</c> / <c>graphql</c>.</summary>
public enum HistoryKind
{
    [JsonStringEnumMemberName("grpc")] Grpc,
    [JsonStringEnumMemberName("graphql")] Graphql
}

/// <summary>
///     One recorded invocation (SPEC-040 §5.1, schema <see cref="CurrentVersion" />). Written after every
///     completed/failed/cancelled call, gRPC or GraphQL. The on-disk form is already redacted (FR-121):
///     header values are <see cref="HistoryHeader" />s whose secret values are <c>«redacted»</c>, and
///     <c>${VAR}</c> placeholders are kept unexpanded. Response bodies are absent unless opt-in capture is on.
/// </summary>
public sealed record HistoryEntry(
    int V,
    string Id,
    DateTimeOffset At,
    HistoryKind Kind,
    HistoryConnection Connection,
    string? WorkspacePath,
    string Method,
    HistoryRequest Request,
    HistoryOutcome Outcome,
    bool Pinned = false)
{
    /// <summary>The history entry schema version this build writes.</summary>
    public const int CurrentVersion = 1;

    /// <summary>
    ///     The literal marker stored in place of a redacted secret header value (FR-121). ASCII, matching
    ///     SPEC-040 §5.1's normative shape, so the on-disk audit can grep for it (and never find a secret).
    /// </summary>
    public const string RedactedMarker = "[redacted]";
}

/// <summary>A connection snapshot for display + replay routing — never security material (SPEC-040 §5.1).</summary>
public sealed record HistoryConnection(string Name, string Address, string Transport, string? TlsProfileName);

/// <summary>A recorded request header. Secret values are stored as <see cref="HistoryEntry.RedactedMarker" />.</summary>
public sealed record HistoryHeader(string Name, string Value);

/// <summary>The redacted request snapshot (SPEC-040 §5.1).</summary>
public sealed record HistoryRequest(
    string BodyFormat,
    string Body,
    bool BodyTruncated,
    IReadOnlyList<HistoryHeader> Headers,
    string? Deadline,
    bool EmitDefaults,
    bool AllowUnknownFields,
    long? MaxSendBytes,
    long? MaxReceiveBytes,
    string? EnvironmentName);

/// <summary>The call outcome (SPEC-040 §5.1). <paramref name="Category" /> mirrors the CLI error categories.</summary>
public sealed record HistoryOutcome(
    string Status,
    string Category,
    int ExitCodeEquivalent,
    long DurationMs,
    int MessagesSent,
    int MessagesReceived,
    string? ResponseBody,
    bool ResponseTruncated,
    string? ErrorMessage);
