using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Google.Protobuf;
using GrpCurl.Net.Studio.ViewModels.Models.Invocation;
using GrpCurl.Net.Studio.ViewModels.Panes;
using GrpCurl.Net.Studio.ViewModels.Services;

namespace GrpCurl.Net.Studio.ViewModels.Documents;

/// <summary>
///     Services a <see cref="StreamRowViewModel" /> needs for its FR-088 context actions: the clipboard,
///     the compact (single-line) formatter used for NDJSON, and the inspector to open a message into.
/// </summary>
public sealed record StreamRowServices(
    IClipboardService Clipboard,
    Func<IMessage, string> CompactFormat,
    IInspector? Inspector);

/// <summary>
///     One row in the streaming event log (FR-081). Holds the raw event; the full pretty-printed body
///     is formatted lazily the first time the row is expanded (ADR-013 deferred formatting). Meta rows
///     (headers/status/warning) render their summary instead of a body. Message rows expose FR-088
///     context actions: copy the body JSON, copy the row as one NDJSON line, or open it in the inspector.
/// </summary>
public sealed partial class StreamRowViewModel : ViewModelBase
{
    private readonly Func<IMessage, string> _formatter;
    private readonly StreamRowServices? _services;

    [ObservableProperty]
    private bool _isExpanded;

    [ObservableProperty]
    private string? _fullJson;

    public StreamRowViewModel(StreamEventModel ev, long deltaMs, Func<IMessage, string> formatter, StreamRowServices? services = null)
    {
        Event = ev;
        DeltaMs = deltaMs;
        _formatter = formatter;
        _services = services;
    }

    public StreamEventModel Event { get; }

    public StreamEventKind Kind => Event.Kind;
    public long Index => Event.Index;
    public DateTimeOffset WallClock => Event.WallClock;
    public long DeltaMs { get; }
    public string Preview => Event.Preview;

    public bool IsMessage => Kind is StreamEventKind.MessageReceived or StreamEventKind.MessageSent;
    public bool HasError => Event.Error is not null;

    /// <summary>FR-088: the body actions (copy JSON / open in viewer) apply only to rows carrying a message.</summary>
    public bool HasMessage => Event.RawMessage is not null;

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

    /// <summary>FR-088: copy the pretty-printed message body.</summary>
    [RelayCommand(CanExecute = nameof(HasMessage))]
    private async Task CopyMessageJson()
    {
        if (_services is { } services && Event.RawMessage is { } message)
        {
            await services.Clipboard.SetTextAsync(_formatter(message));
        }
    }

    /// <summary>FR-088: copy this row as a single NDJSON line (CLI <c>--output json</c> parity).</summary>
    [RelayCommand]
    private async Task CopyAsNdjson()
    {
        if (_services is { } services)
        {
            await services.Clipboard.SetTextAsync(NdjsonStreamFormatter.Format(Event, services.CompactFormat));
        }
    }

    /// <summary>FR-088: open the message body in the inspector ("Open in viewer").</summary>
    [RelayCommand(CanExecute = nameof(HasMessage))]
    private void OpenInViewer()
    {
        if (_services?.Inspector is { } inspector && Event.RawMessage is { } message)
        {
            inspector.ShowMessage(new MessageContent($"Message #{Index}", _formatter(message)));
        }
    }
}
