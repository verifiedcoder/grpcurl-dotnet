using CommunityToolkit.Mvvm.ComponentModel;
using Google.Protobuf;
using GrpCurl.Net.Studio.ViewModels.Models.Invocation;
using System.Collections.ObjectModel;

namespace GrpCurl.Net.Studio.ViewModels.Documents;

/// <summary>
///     The streaming event log (FR-081/085): a ring-buffer <em>view</em> over the event stream. When
///     the buffer overflows, the oldest rows leave the view and a permanent truncation notice appears;
///     the true counters (received/sent/total) always reflect everything that arrived — no silent caps.
/// </summary>
public sealed partial class StreamLogViewModel : ViewModelBase, IOwnsBackgroundWork
{
    private readonly int _capacity;
    private readonly Func<IMessage, string> _formatter;
    private readonly StreamRowServices? _rowServices;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsTruncated), nameof(TruncationNotice))]
    public partial long TotalRows { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RateText))]
    public partial long TotalReceived { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RateText))]
    public partial long TotalSent { get; set; }

    /// <summary>FR-089: wall-clock since the stream began, from the latest event's elapsed timestamp.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ElapsedText), nameof(RateText))]
    public partial long ElapsedMs { get; set; }

    /// <summary>
    ///     The work of rows this log has evicted. The ring drops its oldest row at capacity, and a row's
    ///     copy commands await the singleton clipboard — so the row leaving the collection must not take
    ///     the only handle on a running command with it (PRD-005 re-review round 5, finding 2).
    /// </summary>
    private readonly BackgroundWorkSet _evicted = new();

    public StreamLogViewModel(int ringCapacity, Func<IMessage, string> formatter, StreamRowServices? rowServices = null)
    {
        _capacity = Math.Max(1, ringCapacity);
        _formatter = formatter;
        _rowServices = rowServices;
    }

    public ObservableCollection<StreamRowViewModel> Rows { get; } = [];

    /// <summary>The live rows plus anything the ring has evicted that is still running (PRD-005).</summary>
    void IOwnsBackgroundWork.CollectOwnedWork(List<Task?> tasks)
    {
        WorkGraph.CollectAll(Rows, tasks);

        tasks.Add(_evicted.WhenSettled());
    }

    public bool IsTruncated => TotalRows > Rows.Count;

    public string TruncationNotice => $"showing last {Rows.Count:N0} of {TotalRows:N0} — older rows dropped from view";

    /// <summary>FR-089: footer elapsed (e.g. <c>1.2s</c>).</summary>
    public string ElapsedText => $"{ElapsedMs / 1000.0:0.0}s";

    /// <summary>FR-089: footer throughput in messages per second (received + sent over elapsed).</summary>
    public string RateText
    {
        get
        {
            var seconds = ElapsedMs / 1000.0;
            return seconds > 0 ? $"{(TotalReceived + TotalSent) / seconds:0.0} msg/s" : "—";
        }
    }

    public void Append(IReadOnlyList<StreamEventModel> events)
    {
        foreach (var ev in events)
        {
            Append(ev);
        }
    }

    public void Append(StreamEventModel ev)
    {
        var delta = Math.Max(0, ev.ElapsedMs - ElapsedMs);
        ElapsedMs = ev.ElapsedMs;

        TotalRows++;
        if (ev.Kind == StreamEventKind.MessageReceived)
        {
            TotalReceived++;
        }
        else if (ev.Kind == StreamEventKind.MessageSent)
        {
            TotalSent++;
        }

        if (Rows.Count >= _capacity)
        {
            // Hand the departing row's outstanding work over before it leaves the collection.
            WorkGraph.Retain(_evicted, Rows[0]);

            Rows.RemoveAt(0); // drop oldest from the view (ring buffer)
        }

        Rows.Add(new StreamRowViewModel(ev, delta, _formatter, _rowServices));
    }

    public void Reset()
    {
        WorkGraph.RetainAll(_evicted, Rows);

        Rows.Clear();
        TotalRows = 0;
        TotalReceived = 0;
        TotalSent = 0;
        ElapsedMs = 0;
    }
}
