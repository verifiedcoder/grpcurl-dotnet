using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using Google.Protobuf;
using Google.Rpc;
using Grpc.Core;
using GrpCurl.Net.Exceptions;
using GrpCurl.Net.Invocation;
using GrpCurl.Net.Studio.ViewModels.Models.Invocation;
using GrpCurl.Net.Studio.ViewModels.Services;

namespace GrpCurl.Net.Studio.Services;

/// <summary>
///     Maps a failed invocation (a captured <see cref="UnaryOutcome" />, a reflection
///     <see cref="RpcException" />, or a local resolution/parse failure) plus request context into a
///     UI-free <see cref="ErrorModel" /> (FR-090..099). Decoded <c>google.rpc.Status</c> details
///     become the typed <see cref="ErrorDetailModel" /> hierarchy; the FR-099 JSON envelope is built
///     by constructing Core's <see cref="ErrorEnvelope" /> graph and serialising it with the exact
///     shape and options the CLI's <c>ErrorRenderer.RenderJson</c> uses. Raw exceptions never reach
///     the view-model (SPEC-030 §8).
/// </summary>
internal static class ErrorMapper
{
    private static readonly JsonSerializerOptions EnvelopeJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters =
        {
            new JsonStringEnumConverter(JsonNamingPolicy.CamelCase)
        },
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        WriteIndented = false
    };

    /// <summary>A captured gRPC status failure (the common path: server returned a non-OK status).</summary>
    public static ErrorModel FromOutcome(UnaryOutcome outcome, ErrorContext ctx)
        => FromStatus(outcome.Status.Code, outcome.Status.CodeName, outcome.Status.Detail, outcome.RichDetails, ctx);

    /// <summary>A reflection / transport <see cref="RpcException" /> thrown before the business call ran.</summary>
    public static ErrorModel FromRpcException(RpcException ex, ErrorContext ctx)
        => FromStatus((int)ex.StatusCode, ex.StatusCode.ToString(), ex.Status.Detail, RichStatusDecoder.TryDecode(ex), ctx);

    /// <summary>A schema/resolution failure (e.g. the method was not found on the server).</summary>
    public static ErrorModel FromSchema(string message, ErrorContext ctx)
        => FromLocal(ErrorCategoryKind.Schema, ErrorCategory.Schema, exitCode: 3, statusName: "Schema error", message, ctx);

    /// <summary>An unexpected local failure (malformed request JSON, unset header env var, …).</summary>
    public static ErrorModel FromInternal(string message, ErrorContext ctx)
        => FromLocal(ErrorCategoryKind.Internal, ErrorCategory.Internal, exitCode: 70, statusName: "Error", message, ctx);

    /// <summary>A streaming terminal status (already decoded) → the rich error model.</summary>
    public static ErrorModel FromStreamStatus(int code, string statusName, string detail, StatusDetails? rich, ErrorContext ctx)
        => FromStatus(code, statusName, detail, rich, ctx);

    private static ErrorModel FromStatus(int code, string statusName, string detail, StatusDetails? rich, ErrorContext ctx)
    {
        var severity = StatusSeverityMap.FromCode(code);
        var headline = string.IsNullOrWhiteSpace(detail) ? DefaultHeadline(code, statusName) : detail;
        var hint = HintFor(code, ctx);
        var details = (rich?.Details ?? []).Select(MapDetail).ToList();
        var suggestions = BuildSuggestions(code, detail, ctx);

        var grpc = new RpcErrorInfo
        {
            Code = code,
            Status = statusName,
            Detail = detail,
            StatusDetails = BuildStatusDetailsInfo(rich)
        };

        var json = BuildEnvelope(ErrorCategory.Rpc, exitCode: 64 + code, detail, hint, ctx.Address, ctx.Method, grpc);

        return new ErrorModel(
            ErrorCategoryKind.Rpc, code, statusName, severity, headline, hint,
            ctx.Address, ctx.Method, suggestions, details, json);
    }

    private static ErrorModel FromLocal(ErrorCategoryKind kind, ErrorCategory coreCategory, int exitCode, string statusName, string message, ErrorContext ctx)
    {
        var json = BuildEnvelope(coreCategory, exitCode, message, hint: null, ctx.Address, ctx.Method, grpc: null);
        return new ErrorModel(
            kind, StatusCode: -1, statusName, StatusSeverityMap.FromCategory(kind), message, Hint: null,
            ctx.Address, ctx.Method, Suggestions: [], Details: [], json);
    }

    // ── rich-detail mapping ──────────────────────────────────────────────────

    private static ErrorDetailModel MapDetail(StatusDetail detail) => detail.ParsedMessage switch
    {
        BadRequest br => new BadRequestDetail(
            br.FieldViolations.Select(v => new FieldViolation(v.Field, v.Description)).ToList()),
        RetryInfo ri => new RetryInfoDetail(ri.RetryDelay?.ToTimeSpan() ?? TimeSpan.Zero),
        ErrorInfo ei => new ErrorInfoDetail(
            ei.Reason, ei.Domain, ei.Metadata.Select(kv => new MetadataItem(kv.Key, kv.Value, IsBinary: false)).ToList()),
        QuotaFailure qf => new QuotaFailureDetail(
            qf.Violations.Select(v => new QuotaViolation(v.Subject, v.Description)).ToList()),
        PreconditionFailure pf => new PreconditionFailureDetail(
            pf.Violations.Select(v => new PreconditionViolation(v.Type, v.Subject, v.Description)).ToList()),
        Help help => new HelpDetail(
            help.Links.Select(l => new HelpLink(l.Description, l.Url)).ToList()),
        LocalizedMessage lm => new LocalizedMessageDetail(lm.Locale, lm.Message),
        { } msg => new GenericDetail(detail.TypeUrl, SafeJson(msg)),
        null => new GenericDetail(detail.TypeUrl, string.Empty)
    };

    private static string SafeJson(IMessage message)
    {
        try
        {
            return JsonFormatter.Default.Format(message);
        }
        catch (InvalidOperationException)
        {
            return string.Empty;
        }
    }

    // ── headline / hint / suggestions ────────────────────────────────────────

    private static string DefaultHeadline(int code, string statusName) => code switch
    {
        3 => "Invalid argument",
        4 => "Deadline exceeded",
        5 => "Not found",
        7 => "Permission denied",
        8 => "Resource exhausted",
        12 => "Not implemented",
        14 => "Service unavailable",
        16 => "Unauthenticated",
        _ => statusName
    };

    private static string? HintFor(int code, ErrorContext ctx) => code switch
    {
        1 when ctx.DeadlineSet => "Deadline reached (client).", // FR-098 cancel-vs-deadline disambiguation
        4 => "The server did not respond before the deadline.",
        _ => null
    };

    private static IReadOnlyList<SuggestionModel> BuildSuggestions(int code, string detail, ErrorContext ctx)
    {
        var list = new List<SuggestionModel>();

        switch (code)
        {
            case 14: // UNAVAILABLE
                list.Add(new($"Ensure the server is running and reachable at {ctx.Address}."));
                list.Add(new("If the server speaks plaintext h2c, enable Plaintext on the connection."));
                break;
            case 16: // UNAUTHENTICATED
                list.Add(new("Check the authorization header / credentials."));
                break;
            case 7: // PERMISSION_DENIED
                list.Add(new("Verify the caller is permitted to invoke this method."));
                break;
            case 4: // DEADLINE_EXCEEDED
                list.Add(new("Increase the deadline or investigate server latency."));
                break;
            case 12: // UNIMPLEMENTED
                list.Add(new("Confirm the method name and that the server implements it."));
                break;
            case 8: // RESOURCE_EXHAUSTED
                list.Add(new("Back off and retry; you may be hitting a rate limit or quota."));
                break;
            case 3: // INVALID_ARGUMENT
                list.Add(new("Check the request body against the method's input schema."));
                break;
        }

        // FR-096: surface a TLS certificate-revocation hint when the detail mentions it.
        if (MentionsRevocation(detail))
        {
            list.Add(new("The TLS certificate may be revoked or its revocation status unreachable; review the connection's TLS settings."));
        }

        // SEC-013: a custom-CA chain whose revocation status can't be reached (no CRL/OCSP endpoint, the
        // common private-CA case) fails validation. Point the user at the revocation-mode escape hatch.
        if (MentionsRevocationUnknown(detail))
        {
            list.Add(new(
                "If this server uses a private CA without CRL/OCSP endpoints, set revocation mode "
                + "'offline' or 'nocheck' in the connection's TLS profile."));
        }

        // FR-097: a proxy environment variable may be intercepting a transport failure.
        if ((code == 14 || code == 2) && ProxyEnvActive() is { } proxyVar)
        {
            list.Add(new($"A proxy environment variable ({proxyVar}) is set and may be intercepting the connection."));
        }

        return list;
    }

    private static bool MentionsRevocation(string detail)
        => detail.Contains("revoc", StringComparison.OrdinalIgnoreCase)
           || detail.Contains("revoked", StringComparison.OrdinalIgnoreCase);

    // The chain-status text Core/SslStream surface when the revocation endpoint is unreachable
    // (RevocationStatusUnknown / OfflineRevocation) — the private-CA-without-CRL signature.
    private static bool MentionsRevocationUnknown(string detail)
        => detail.Contains("RevocationStatusUnknown", StringComparison.OrdinalIgnoreCase)
           || detail.Contains("OfflineRevocation", StringComparison.OrdinalIgnoreCase)
           || detail.Contains("unable to determine", StringComparison.OrdinalIgnoreCase)
           || (detail.Contains("revocation", StringComparison.OrdinalIgnoreCase)
               && detail.Contains("offline", StringComparison.OrdinalIgnoreCase));

    private static string? ProxyEnvActive()
    {
        foreach (var name in new[] { "HTTPS_PROXY", "https_proxy", "HTTP_PROXY", "http_proxy", "ALL_PROXY", "all_proxy" })
        {
            if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(name)))
            {
                return name;
            }
        }

        return null;
    }

    // ── FR-099 JSON envelope (CLI ErrorRenderer.RenderJson parity) ────────────

    private static RpcStatusDetailsInfo? BuildStatusDetailsInfo(StatusDetails? rich)
    {
        if (rich is null)
        {
            return null;
        }

        var entries = rich.Details.Select(d => new RpcStatusDetailEntry
        {
            TypeUrl = d.TypeUrl,
            RawBase64 = d.ParsedMessage is null ? Convert.ToBase64String(d.RawValue) : null,
            Json = d.ParsedMessage is { } parsed ? JsonFormatter.Default.Format(parsed) : null
        }).ToList();

        return new RpcStatusDetailsInfo
        {
            Code = rich.Code,
            Message = rich.Message,
            Details = entries
        };
    }

    private static string BuildEnvelope(ErrorCategory category, int exitCode, string message, string? hint, string? address, string? method, RpcErrorInfo? grpc)
    {
        var envelope = new ErrorEnvelope
        {
            Category = category,
            ExitCode = exitCode,
            Message = message,
            Hint = hint,
            Address = address,
            Method = method,
            Grpc = grpc
        };

        // Mirrors ErrorRenderer.RenderJson exactly so the Studio "Copy as JSON" payload is byte-parity with the CLI.
        var payload = new
        {
            kind = "error",
            category = envelope.Category,
            exitCode = envelope.ExitCode,
            message = envelope.Message,
            hint = envelope.Hint,
            address = envelope.Address,
            method = envelope.Method,
            grpc = envelope.Grpc
        };

        return JsonSerializer.Serialize(payload, EnvelopeJsonOptions);
    }
}

/// <summary>Request context threaded into the error model (the call's method, target, and whether a deadline was set).</summary>
internal readonly record struct ErrorContext(string Method, string Address, bool DeadlineSet);
