using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.ObjectModel;

namespace GrpCurl.Net.Studio.ViewModels.Documents;

/// <summary>
///     The GraphQL subscription console (GQL-060..065): a ring-buffer view over the streamed envelopes,
///     mirroring the invocation streaming console's no-silent-caps contract (ADR-013). The counters always
///     reflect everything received; when the buffer overflows, the oldest rows leave the view and a
///     truncation notice appears. Fed by the bounded/batched <c>StreamDispatchPump</c> (SPEC-030 §6).
/// </summary>
public sealed partial class GraphQlStreamLogViewModel : ViewModelBase
{
    private readonly int _capacity;

    public GraphQlStreamLogViewModel(int ringCapacity = 10_000) => _capacity = Math.Max(1, ringCapacity);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsTruncated), nameof(TruncationNotice))]
    public partial long TotalRows { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RateText))]
    public partial long TotalReceived { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ElapsedText), nameof(RateText))]
    public partial long ElapsedMs { get; set; }

    public ObservableCollection<GraphQlStreamRow> Rows { get; } = [];

    public bool IsTruncated => TotalRows > Rows.Count;

    public string TruncationNotice => $"showing last {Rows.Count:N0} of {TotalRows:N0} — older rows dropped from view";

    /// <summary>GQL-065: per-stream elapsed (e.g. <c>1.2s</c>).</summary>
    public string ElapsedText => $"{ElapsedMs / 1000.0:0.0}s";

    /// <summary>GQL-065: per-stream throughput (messages per second).</summary>
    public string RateText
    {
        get
        {
            var seconds = ElapsedMs / 1000.0;
            return seconds > 0 ? $"{TotalReceived / seconds:0.0} msg/s" : "—";
        }
    }

    /// <summary>Appends one streamed envelope row, updating counters + the ring buffer.</summary>
    public void Append(GraphQlStreamRow row)
    {
        ElapsedMs = row.ElapsedMs;
        TotalRows++;

        if (!row.IsStatus)
        {
            TotalReceived++;
        }

        if (Rows.Count >= _capacity)
        {
            Rows.RemoveAt(0); // ring buffer — oldest row leaves the view
        }

        Rows.Add(row);
    }

    /// <summary>Appends a meta/status line (GQL-062), e.g. "cancelled after N messages".</summary>
    public void AppendStatus(string text) => Append(new GraphQlStreamRow(-1, ElapsedMs, text, isStatus: true));

    public void Reset()
    {
        Rows.Clear();
        TotalRows = 0;
        TotalReceived = 0;
        ElapsedMs = 0;
    }
}
