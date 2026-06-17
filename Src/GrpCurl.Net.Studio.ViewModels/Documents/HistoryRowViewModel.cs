using CommunityToolkit.Mvvm.ComponentModel;
using GrpCurl.Net.Studio.ViewModels.Models.History;

namespace GrpCurl.Net.Studio.ViewModels.Documents;

/// <summary>One row in the History grid (FR-122): a redacted <see cref="HistoryEntry" /> shown for
/// browse/filter/replay, with a replayability badge (SPEC-040 §5.2) and a selection flag.</summary>
public sealed partial class HistoryRowViewModel : ViewModelBase
{
    [ObservableProperty]
    private bool _isSelected;

    public HistoryRowViewModel(HistoryEntry entry, bool replayable)
    {
        Entry = entry;
        Replayable = replayable;
    }

    public HistoryEntry Entry { get; }

    public string Id => Entry.Id;

    public string TimeText => Entry.At.LocalDateTime.ToString("yyyy-MM-dd HH:mm:ss");

    public string Method => Entry.Method;

    public string ConnectionName => Entry.Connection.Name;

    public string Status => Entry.Outcome.Status;

    public bool IsError => Entry.Outcome.ExitCodeEquivalent != 0;

    public string DurationText => $"{Entry.Outcome.DurationMs} ms";

    public string KindText => Entry.Kind == HistoryKind.Grpc ? "gRPC" : "GraphQL";

    public bool Pinned => Entry.Pinned;

    /// <summary>SPEC-040 §5.2: false when the body was truncated or the connection no longer resolves.</summary>
    public bool Replayable { get; }

    public string ReplayBadge => Replayable ? "replayable" : "partial";
}
