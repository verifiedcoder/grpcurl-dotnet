using CommunityToolkit.Mvvm.ComponentModel;
using Google.Protobuf;
using GrpCurl.Net.Studio.ViewModels.Models.Invocation;

namespace GrpCurl.Net.Studio.ViewModels.Documents;

/// <summary>
///     One row in the streaming event log (FR-081). Holds the raw event; the full pretty-printed body
///     is formatted lazily the first time the row is expanded (ADR-013 deferred formatting). Meta rows
///     (headers/status/warning) render their summary instead of a body.
/// </summary>
public sealed partial class StreamRowViewModel : ViewModelBase
{
    private readonly Func<IMessage, string> _formatter;

    [ObservableProperty]
    private bool _isExpanded;

    [ObservableProperty]
    private string? _fullJson;

    public StreamRowViewModel(StreamEventModel ev, long deltaMs, Func<IMessage, string> formatter)
    {
        Event = ev;
        DeltaMs = deltaMs;
        _formatter = formatter;
    }

    public StreamEventModel Event { get; }

    public StreamEventKind Kind => Event.Kind;
    public long Index => Event.Index;
    public DateTimeOffset WallClock => Event.WallClock;
    public long DeltaMs { get; }
    public string Preview => Event.Preview;

    public bool IsMessage => Kind is StreamEventKind.MessageReceived or StreamEventKind.MessageSent;
    public bool HasError => Event.Error is not null;

    /// <summary>Direction/kind glyph for the row gutter (FR-083).</summary>
    public string Glyph => Kind switch
    {
        StreamEventKind.MessageReceived => "◀",
        StreamEventKind.MessageSent => "▶",
        StreamEventKind.Headers => "≡",
        StreamEventKind.Status => "●",
        StreamEventKind.Warning => "⚠",
        _ => " "
    };

    partial void OnIsExpandedChanged(bool value)
    {
        if (value && FullJson is null && Event.RawMessage is { } message)
        {
            FullJson = _formatter(message);
        }
    }
}
