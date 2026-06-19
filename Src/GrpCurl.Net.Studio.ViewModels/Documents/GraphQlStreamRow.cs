using CommunityToolkit.Mvvm.ComponentModel;

namespace GrpCurl.Net.Studio.ViewModels.Documents;

/// <summary>
///     One row in the GraphQL subscription console (GQL-061): either a streamed GraphQL envelope or a
///     meta/status line (GQL-062). Each message row carries the complete NDJSON envelope; the view shows
///     a one-line preview and expands to the full text.
/// </summary>
public sealed partial class GraphQlStreamRow : ObservableObject
{
    public GraphQlStreamRow(long index, long elapsedMs, string json, bool isStatus = false)
    {
        Index = index;
        ElapsedMs = elapsedMs;
        Json = json;
        IsStatus = isStatus;
    }

    public long Index { get; }

    public long ElapsedMs { get; }

    /// <summary>The complete envelope (a message row) or the status text (a meta row).</summary>
    public string Json { get; }

    public bool IsStatus { get; }

    public string Glyph => IsStatus ? "●" : "◀";

    public string Preview => Json.Length <= 200 ? Json : Json[..200] + "…";

    [ObservableProperty]
    public partial bool IsExpanded { get; set; }
}
