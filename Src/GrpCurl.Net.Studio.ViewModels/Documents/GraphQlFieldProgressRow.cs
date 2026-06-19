using CommunityToolkit.Mvvm.ComponentModel;
using GrpCurl.Net.Studio.ViewModels.Models.GraphQl;
using System.Globalization;

namespace GrpCurl.Net.Studio.ViewModels.Documents;

/// <summary>
///     One row in the per-root-field progress list (GQL-024 / AC-6): the response key plus a live state
///     and elapsed readout, so the user can watch the bounded-4 parallel window move.
/// </summary>
public sealed partial class GraphQlFieldProgressRow : ObservableObject
{
    public GraphQlFieldProgressRow(int index, string responseKey)
    {
        Index = index;
        ResponseKey = responseKey;
    }

    public int Index { get; }

    public string ResponseKey { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Glyph))]
    public partial GraphQlFieldState State { get; set; } = GraphQlFieldState.Queued;

    [ObservableProperty]
    public partial string? ElapsedText { get; set; }

    /// <summary>A compact state indicator for the row (queued / running / done / failed).</summary>
    public string Glyph => State switch
    {
        GraphQlFieldState.Queued => "•",
        GraphQlFieldState.InFlight => "▸",
        GraphQlFieldState.Done => "✓",
        GraphQlFieldState.Failed => "✗",
        _ => "•"
    };

    /// <summary>Applies a progress notification to this row.</summary>
    public void Apply(GraphQlFieldProgress progress)
    {
        State = progress.State;

        if (progress.ElapsedMs is { } ms)
        {
            ElapsedText = string.Create(CultureInfo.InvariantCulture, $"{ms:0} ms");
        }
    }
}
