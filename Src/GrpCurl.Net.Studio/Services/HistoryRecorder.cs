using GrpCurl.Net.Studio.ViewModels.Models.Connections;
using GrpCurl.Net.Studio.ViewModels.Models.GraphQl;
using GrpCurl.Net.Studio.ViewModels.Models.History;
using GrpCurl.Net.Studio.ViewModels.Models.Invocation;
using GrpCurl.Net.Studio.ViewModels.Services;
using GrpCurl.Net.Utilities;
using System.Text;

namespace GrpCurl.Net.Studio.Services;

/// <summary>
///     Default <see cref="IHistoryRecorder" /> (FR-120/121). Snapshots the connection (display fields
///     only), redacts request headers through Core's <see cref="SecretRedactor" /> (secret-classified
///     names become <see cref="HistoryEntry.RedactedMarker" />; <c>${VAR}</c> placeholders are kept
///     unexpanded), caps bodies at the configured size, and appends through <see cref="IHistoryStore" />.
///     Response bodies are stored only when opt-in capture is enabled.
/// </summary>
internal sealed class HistoryRecorder(
    IHistoryStore store,
    ISettingsStore settings,
    IWorkspaceStore? workspace = null,
    ITlsProfileStore? profiles = null) : IHistoryRecorder
{
    public async Task RecordUnaryAsync(InvocationRequestModel request, InvocationResultModel result, CancellationToken cancellationToken = default)
    {
        var history = settings.Current.History;

        if (!history.Enabled)
        {
            return;
        }

        var body = Cap(request.RequestJson, history.ResponseCapBytes, out var bodyTruncated);
        var responseBody = history.CaptureResponses && result.ResponseJson is not null
            ? Cap(result.ResponseJson, history.ResponseCapBytes, out _)
            : null;

        var requestSnapshot = new HistoryRequest(
            BodyFormat(request.BodyFormat), body, bodyTruncated, RedactHeaders(request.Headers),
            request.Deadline, request.EmitDefaults, request.AllowUnknownFields,
            ParseBytes(request.MaxMessageSize), MaxReceiveBytes: null, EnvironmentName: null);

        var outcome = new HistoryOutcome(
            result.Status.CodeName, Category(result), ExitCode(result),
            TotalMs(result.Timing), MessagesSent: 1, MessagesReceived: result.Ok && result.ResponseJson is not null ? 1 : 0,
            responseBody, ResponseTruncated: false, result.ErrorMessage);

        await store.AppendAsync(
            Build(HistoryKind.Grpc, request.Connection, request.MethodSymbol, requestSnapshot, outcome),
            cancellationToken).ConfigureAwait(false);
    }

    public async Task RecordStreamAsync(
        StreamRequestModel request, InvocationStatusModel status, long durationMs,
        int messagesSent, int messagesReceived, CancellationToken cancellationToken = default)
    {
        var history = settings.Current.History;

        if (!history.Enabled)
        {
            return;
        }

        // Streams are not stored in the history body — their messages live in the explicit NDJSON export
        // (FR-087). The entry records the call, its counts, and its terminal status.
        var requestSnapshot = new HistoryRequest(
            BodyFormat(request.BodyFormat), Body: string.Empty, BodyTruncated: false, RedactHeaders(request.Headers),
            request.Deadline, request.EmitDefaults, request.AllowUnknownFields,
            ParseBytes(request.MaxMessageSize), MaxReceiveBytes: null, EnvironmentName: null);

        var category = status.Code == 0 ? "success" : status.Code == 1 ? "cancelled" : "rpc-error";
        var outcome = new HistoryOutcome(
            status.CodeName, category, status.Code == 0 ? 0 : 64 + status.Code,
            durationMs, messagesSent, messagesReceived,
            ResponseBody: null, ResponseTruncated: false, status.Code == 0 ? null : status.Detail);

        await store.AppendAsync(
            Build(HistoryKind.Grpc, request.Connection, request.MethodSymbol, requestSnapshot, outcome),
            cancellationToken).ConfigureAwait(false);
    }

    public async Task RecordGraphQlAsync(GraphQlHistoryContext context, CancellationToken cancellationToken = default)
    {
        var history = settings.Current.History;

        if (!history.Enabled)
        {
            return;
        }

        var body = Cap(context.Document, history.ResponseCapBytes, out var bodyTruncated);
        var responseBody = history.CaptureResponses && context.ResponseEnvelope is not null
            ? Cap(context.ResponseEnvelope, history.ResponseCapBytes, out _)
            : null;

        // GraphQL documents are not protobuf bodies, so BodyFormat records the surface ("graphql").
        var requestSnapshot = new HistoryRequest(
            "graphql", body, bodyTruncated, RedactHeaders(context.Headers),
            context.Deadline, context.EmitDefaults, context.AllowUnknownFields,
            MaxSendBytes: null, MaxReceiveBytes: null, context.EnvironmentName);

        var outcome = new HistoryOutcome(
            context.Status, context.Category, context.Ok ? 0 : 1,
            context.DurationMs, MessagesSent: 1, MessagesReceived: context.Ok ? 1 : 0,
            responseBody, ResponseTruncated: false, context.ErrorMessage);

        await store.AppendAsync(
            Build(HistoryKind.Graphql, context.Connection, context.OperationLabel, requestSnapshot, outcome),
            cancellationToken).ConfigureAwait(false);
    }

    private HistoryEntry Build(HistoryKind kind, SavedConnection connection, string method, HistoryRequest request, HistoryOutcome outcome)
        => new(
            HistoryEntry.CurrentVersion, Guid.NewGuid().ToString("D"), DateTimeOffset.UtcNow, kind,
            Snapshot(connection), workspace?.CurrentPath, method, request, outcome);

    private HistoryConnection Snapshot(SavedConnection connection) => new(
        connection.Name,
        connection.Address,
        connection.Transport == TransportMode.Plaintext ? "plaintext" : "tls",
        TlsProfileName(connection));

    private string? TlsProfileName(SavedConnection connection)
        => connection.TlsProfileId is { } id
            ? profiles?.Profiles.FirstOrDefault(p => p.Id == id)?.Name
            : null;

    // FR-121: secret-classified header values are dropped to the marker; everything else (including
    // ${VAR} placeholders) is stored verbatim, unexpanded — the file never holds a secret literal.
    private static IReadOnlyList<HistoryHeader> RedactHeaders(IReadOnlyList<HeaderEntry> headers)
        => headers.Select(h => new HistoryHeader(
            h.Name, SecretRedactor.ShouldRedact(h.Name) ? HistoryEntry.RedactedMarker : h.Value)).ToList();

    private static string Cap(string text, int capBytes, out bool truncated)
    {
        if (Encoding.UTF8.GetByteCount(text) <= capBytes)
        {
            truncated = false;
            return text;
        }

        var builder = new StringBuilder();
        var bytes = 0;

        foreach (var rune in text.EnumerateRunes())
        {
            if (bytes + rune.Utf8SequenceLength > capBytes)
            {
                break; // cut at the last whole UTF-8 sequence within the cap
            }

            bytes += rune.Utf8SequenceLength;
            _ = builder.Append(rune.ToString());
        }

        truncated = true;
        return builder.ToString();
    }

    private static string BodyFormat(RequestBodyFormat format) => format == RequestBodyFormat.Text ? "text" : "json";

    private static long TotalMs(TimingModel timing)
    {
        var total = timing.Phases.FirstOrDefault(p => p.Phase == "total")?.Duration
                    ?? timing.Phases.Aggregate(TimeSpan.Zero, (acc, p) => acc + p.Duration);
        return (long)total.TotalMilliseconds;
    }

    private static long? ParseBytes(string? value)
        => long.TryParse(value, out var parsed) && parsed > 0 ? parsed : null;

    private static string Category(InvocationResultModel result) => result.Ok
        ? "success"
        : result.Error?.Category switch
        {
            ErrorCategoryKind.Rpc => "rpc-error",
            ErrorCategoryKind.Network or ErrorCategoryKind.Timeout => "transport",
            ErrorCategoryKind.Cancelled => "cancelled",
            ErrorCategoryKind.Usage or ErrorCategoryKind.Schema => "input",
            _ => "internal"
        };

    private static int ExitCode(InvocationResultModel result)
        => result.Ok ? 0 : 64 + result.Status.Code;
}
