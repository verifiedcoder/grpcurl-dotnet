namespace GrpCurl.Net.Studio.ViewModels.Panes;

/// <summary>The kind of operation a console row represents (FR-004), for its leading label.</summary>
public enum ConsoleActivityKind
{
    Invocation,
    Descriptor,
    Connection,
    Export
}

/// <summary>
///     FR-004: one operation recorded in the console — its title, start time, outcome, the wall-clock total
///     shown inline, and the per-phase breakdown surfaced in the inspector when the row is selected (FR-114).
///     <see cref="Method" /> is the operation title (a method FQN for invocations, a connection name for
///     descriptor/connection ops); <see cref="StatusName" /> is the outcome text.
/// </summary>
public sealed record ConsoleCallActivity(
    string Method,
    int StatusCode,
    string StatusName,
    bool IsError,
    string TotalText,
    IReadOnlyList<CallTimingPhase> Phases,
    ConsoleActivityKind Kind = ConsoleActivityKind.Invocation,
    DateTimeOffset At = default);

/// <summary>A console row wrapping a <see cref="ConsoleCallActivity" /> with display + inspector content.</summary>
public sealed class ConsoleCallRowViewModel : ViewModelBase
{
    public ConsoleCallRowViewModel(ConsoleCallActivity activity) => Activity = activity;

    public ConsoleCallActivity Activity { get; }

    public string Method => Activity.Method;

    public string StatusName => Activity.StatusName;

    public bool IsError => Activity.IsError;

    public string TotalText => Activity.TotalText;

    /// <summary>The leading kind label (FR-004): invoke / describe / connect / export.</summary>
    public string KindLabel => Activity.Kind switch
    {
        ConsoleActivityKind.Descriptor => "describe",
        ConsoleActivityKind.Connection => "connect",
        ConsoleActivityKind.Export => "export",
        _ => "invoke"
    };

    /// <summary>Start time (FR-004), blank when not recorded (older invocation rows).</summary>
    public string TimeText => Activity.At == default ? string.Empty : Activity.At.ToLocalTime().ToString("HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>Compact one-line summary: <c>pkg.Svc/Go · OK · 12 ms</c> (FR-114 inline total).</summary>
    public string Display => $"{Activity.Method} · {Activity.StatusName} · {Activity.TotalText}";

    /// <summary>The breakdown shown in the inspector when this row is selected.</summary>
    public CallTimingContent Timing
        => new($"{Activity.Method} — timing", Activity.TotalText, Activity.IsError, Activity.Phases);
}
