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
    private long _lastElapsedMs;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsTruncated), nameof(TruncationNotice))]
    private long _totalRows;

    [ObservableProperty]
    private long _totalReceived;

    [ObservableProperty]
    private long _totalSent;

    public StreamLogViewModel(int ringCapacity, Func<IMessage, string> formatter, StreamRowServices? rowServices = null)
    {
        _capacity = Math.Max(1, ringCapacity);
        _formatter = formatter;
        _rowServices = rowServices;
    }

    public ObservableCollection<StreamRowViewModel> Rows { get; } = [];

    public bool IsTruncated => TotalRows > Rows.Count;

    public string TruncationNotice => $"showing last {Rows.Count:N0} of {TotalRows:N0} — older rows dropped from view";

    public void Append(IReadOnlyList<StreamEventModel> events)
    {
        foreach (var ev in events)
        {
            Append(ev);
        }
    }

    public void Append(StreamEventModel ev)
    {
        var delta = Math.Max(0, ev.ElapsedMs - _lastElapsedMs);
        _lastElapsedMs = ev.ElapsedMs;

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
        _lastElapsedMs = 0;
    }
}
