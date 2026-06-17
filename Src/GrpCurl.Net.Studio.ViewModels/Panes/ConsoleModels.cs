namespace GrpCurl.Net.Studio.ViewModels.Panes;

/// <summary>
///     FR-114: one completed call recorded in the console — its target method, terminal status, the
///     wall-clock total shown inline, and the per-phase breakdown surfaced in the inspector when the
///     row is selected.
/// </summary>
public sealed record ConsoleCallActivity(
    string Method,
    int StatusCode,
    string StatusName,
    bool IsError,
    string TotalText,
    IReadOnlyList<CallTimingPhase> Phases);

/// <summary>A console row wrapping a <see cref="ConsoleCallActivity" /> with display + inspector content.</summary>
public sealed class ConsoleCallRowViewModel : ViewModelBase
{
    public ConsoleCallRowViewModel(ConsoleCallActivity activity) => Activity = activity;

    public ConsoleCallActivity Activity { get; }

    public string Method => Activity.Method;

    public string StatusName => Activity.StatusName;

    public bool IsError => Activity.IsError;

    public string TotalText => Activity.TotalText;

    /// <summary>Compact one-line summary: <c>pkg.Svc/Go · OK · 12 ms</c> (FR-114 inline total).</summary>
    public string Display => $"{Activity.Method} · {Activity.StatusName} · {Activity.TotalText}";

    /// <summary>The breakdown shown in the inspector when this row is selected.</summary>
    public CallTimingContent Timing
        => new($"{Activity.Method} — timing", Activity.TotalText, Activity.IsError, Activity.Phases);
}
