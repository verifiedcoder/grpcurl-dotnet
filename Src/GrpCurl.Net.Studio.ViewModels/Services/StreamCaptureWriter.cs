using Google.Protobuf;
using GrpCurl.Net.Studio.ViewModels.Models.Invocation;
using System.Text;

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

    private volatile bool _disposed;

    public StreamCaptureWriter(TextWriter writer, Func<IMessage, string> compactFormat)
    {
        _writer = writer;
        _compactFormat = compactFormat;
    }

    /// <summary>Bytes written so far (for the live capture-size readout).</summary>
    public long BytesWritten { get; private set; }

    /// <summary>
    ///     Writes one event, or does nothing once disposed.
    ///     <para>
    ///         The no-op is what lets the owning tab dispose this while a stream is still pumping
    ///         (PRD-005). The pump reads the writer into a local and awaits this off the UI thread, so
    ///         closing a tab mid-stream would otherwise throw <see cref="ObjectDisposedException" />
    ///         into a fire-and-forget task — an unobserved teardown failure, and precisely the kind of
    ///         noise PRD-023 is trying to stop suppressing. Dropping capture lines for a tab the user
    ///         is closing is the intended trade.
    ///     </para>
    /// </summary>
    public async Task WriteAsync(StreamEventModel ev)
    {
        if (_disposed)
        {
            return;
        }

        var line = NdjsonStreamFormatter.Format(ev, _compactFormat);
        await _writer.WriteLineAsync(line).ConfigureAwait(false);
        await _writer.FlushAsync().ConfigureAwait(false);
        BytesWritten += Encoding.UTF8.GetByteCount(line) + Environment.NewLine.Length;
    }

    /// <summary>Idempotent: the owning tab and the capture toggle can both reach it.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        _writer.Dispose();
    }
}
