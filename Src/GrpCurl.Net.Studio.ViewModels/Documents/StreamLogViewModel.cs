using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Google.Protobuf;
using GrpCurl.Net.Studio.ViewModels.Models.Invocation;

namespace GrpCurl.Net.Studio.ViewModels.Documents;

/// <summary>
///     The streaming event log (FR-081/085): a ring-buffer <em>view</em> over the event stream. When
///     the buffer overflows, the oldest rows leave the view and a permanent truncation notice appears;
///     the true counters (received/sent/total) always reflect everything that arrived — no silent caps.
/// </summary>
public sealed partial class StreamLogViewModel : ViewModelBase
{
    private readonly int _capacity;
    private readonly Func<IMessage, string> _formatter;
    private readonly StreamRowServices? _rowServices;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsTruncated), nameof(TruncationNotice))]
    private long _totalRows;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RateText))]
    private long _totalReceived;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RateText))]
    private long _totalSent;

    /// <summary>FR-089: wall-clock since the stream began, from the latest event's elapsed timestamp.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ElapsedText), nameof(RateText))]
    private long _elapsedMs;

    public StreamLogViewModel(int ringCapacity, Func<IMessage, string> formatter, StreamRowServices? rowServices = null)
    {
        _capacity = Math.Max(1, ringCapacity);
        _formatter = formatter;
        _rowServices = rowServices;
    }

    public ObservableCollection<StreamRowViewModel> Rows { get; } = [];

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
            Rows.RemoveAt(0); // drop oldest from the view (ring buffer)
        }

        Rows.Add(new StreamRowViewModel(ev, delta, _formatter, _rowServices));
    }

    public void Reset()
    {
        Rows.Clear();
        TotalRows = 0;
        TotalReceived = 0;
        TotalSent = 0;
        ElapsedMs = 0;
    }
}
