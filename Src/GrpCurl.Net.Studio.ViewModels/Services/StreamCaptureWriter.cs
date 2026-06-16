using System.Text;
using Google.Protobuf;
using GrpCurl.Net.Studio.ViewModels.Models.Invocation;

namespace GrpCurl.Net.Studio.ViewModels.Services;

/// <summary>
///     Spills every streaming event to an NDJSON sink as it arrives (FR-086), so ring-buffer overflow
///     never loses data — the capture taps the stream <em>before</em> the ring. Flushes per line so a
///     crash keeps what arrived. Writes happen on the producer flow (off the UI thread).
/// </summary>
public sealed class StreamCaptureWriter : IDisposable
{
    private readonly TextWriter _writer;
    private readonly Func<IMessage, string> _compactFormat;

    public StreamCaptureWriter(TextWriter writer, Func<IMessage, string> compactFormat)
    {
        _writer = writer;
        _compactFormat = compactFormat;
    }

    /// <summary>Bytes written so far (for the live capture-size readout).</summary>
    public long BytesWritten { get; private set; }

    public async Task WriteAsync(StreamEventModel ev)
    {
        var line = NdjsonStreamFormatter.Format(ev, _compactFormat);
        await _writer.WriteLineAsync(line).ConfigureAwait(false);
        await _writer.FlushAsync().ConfigureAwait(false);
        BytesWritten += Encoding.UTF8.GetByteCount(line) + Environment.NewLine.Length;
    }

    public void Dispose() => _writer.Dispose();
}
