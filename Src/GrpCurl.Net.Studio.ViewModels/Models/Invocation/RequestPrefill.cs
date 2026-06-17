namespace GrpCurl.Net.Studio.ViewModels.Models.Invocation;

/// <summary>
///     A header to pre-fill into an invocation tab. <see cref="RequiresValue" /> marks a header whose value
///     could not be restored (a redacted secret from history, FR-123) — the name is known, the value must be
///     re-entered.
/// </summary>
public sealed record PrefillHeader(string Name, string Value, bool IsBin, bool RequiresValue = false);

/// <summary>
///     The state to pre-fill an invocation tab with (FR-123 replay, FR-145 saved requests). Carries the body,
///     format, headers, and options but no tab identity — opening from a saved request additionally binds the
///     tab to that request, whereas a replay opens a plain draft.
/// </summary>
public sealed record RequestPrefill(
    string Body,
    RequestBodyFormat BodyFormat,
    IReadOnlyList<PrefillHeader> Headers,
    string? Deadline = null,
    bool EmitDefaults = false,
    bool AllowUnknownFields = true,
    string? MaxMessageSize = null,
    string? Title = null);
