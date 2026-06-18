using System.Text.Json.Serialization;

namespace GrpCurl.Net.Studio.ViewModels.Models.Diagnostics;

/// <summary>Severity of a diagnostics log entry (mirrors <c>Microsoft.Extensions.Logging.LogLevel</c>).</summary>
public enum DiagnosticsLevel
{
    [JsonStringEnumMemberName("trace")] Trace,
    [JsonStringEnumMemberName("debug")] Debug,
    [JsonStringEnumMemberName("info")] Information,
    [JsonStringEnumMemberName("warn")] Warning,
    [JsonStringEnumMemberName("error")] Error,
    [JsonStringEnumMemberName("critical")] Critical
}

/// <summary>
///     One line in the diagnostics log (SPEC-030 §9, FR-155). Stored as NDJSON in the app-data log file.
///     Carries only a category, level, timestamp, and message — <strong>never header values</strong>
///     (SEC-031: log call sites pass header names only).
/// </summary>
public sealed record DiagnosticsLogEntry(
    DateTimeOffset At,
    DiagnosticsLevel Level,
    string Category,
    string Message);
