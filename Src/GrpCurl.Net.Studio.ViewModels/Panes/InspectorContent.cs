namespace GrpCurl.Net.Studio.ViewModels.Panes;

/// <summary>
///     The discriminated content the <see cref="InspectorViewModel" /> renders. The pane is a shared
///     detail surface: it shows a method signature (FR-020), a single streamed message routed from the
///     event log (FR-088 "Open in viewer"), or the phase breakdown of a completed call selected in the
///     console (FR-114). Each variant is a UI-free record; the view templates one per concrete type.
/// </summary>
public abstract record InspectorContent;

/// <summary>The empty state: "Select an item to inspect." (the inspector's resting content).</summary>
public sealed record EmptyInspectorContent : InspectorContent
{
    public static EmptyInspectorContent Instance { get; } = new();
}

/// <summary>FR-020: the selected method's signature (request → response, streaming shape).</summary>
public sealed record MethodSignatureContent(
    string FullName,
    string Name,
    string Shape,
    string InputType,
    string OutputType) : InspectorContent;

/// <summary>FR-088: a single streamed message body opened from the event log into the inspector.</summary>
public sealed record MessageContent(string Title, string Json) : InspectorContent;

/// <summary>FR-114: a completed call's total plus its per-phase breakdown.</summary>
public sealed record CallTimingContent(
    string Title,
    string TotalText,
    bool IsError,
    IReadOnlyList<CallTimingPhase> Phases) : InspectorContent;

/// <summary>One phase row in a <see cref="CallTimingContent" /> (e.g. <c>descriptor 30 ms · 30%</c>).</summary>
public sealed record CallTimingPhase(string Phase, string DurationText, double Fraction)
{
    /// <summary>Percentage label for the bar; blank for the synthetic <c>total</c> row.</summary>
    public string PercentText => Phase == "total" ? string.Empty : $"{Fraction * 100:0}%";
}
