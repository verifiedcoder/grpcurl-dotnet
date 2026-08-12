using Google.Protobuf;
using GrpCurl.Net.Studio.ViewModels.Models.Invocation;
using System.Text;

namespace GrpCurl.Net.Studio.ViewModels.Services;

/// <summary>
///     Spills every streaming event to an NDJSON sink as it arrives (FR-086), so ring-buffer overflow
///     never loses data — the capture taps the stream <em>before</em> the ring. Flushes per line so a
///     crash keeps what arrived. Writes happen on the producer flow (off the UI thread).
/// </summary>
/// <remarks>
///     Disposal is deferred, not immediate, because the owning tab can be closed while the stream pump
///     is mid-write. A plain disposed flag checked at the top of <see cref="WriteAsync" /> does not
///     help: <see cref="Dispose" /> can land after that check and release the underlying writer while
///     <c>WriteLineAsync</c> or <c>FlushAsync</c> is still awaiting, and the write then throws
///     <see cref="ObjectDisposedException" /> into a fire-and-forget task anyway. Marking the flag
///     <c>volatile</c> changes nothing — the gap is between the check and the use, not a visibility
///     problem (PRD-005 review, finding 2).
///     <para>
///         So writes run inside a gate, and <see cref="Dispose" /> hands the underlying writer over to
///         whoever leaves the gate last. It never blocks the caller: a tab close returns immediately and
///         the sink closes when the write in flight finishes.
///     </para>
/// </remarks>
public sealed class StreamCaptureWriter : IDisposable
{
    private readonly Func<IMessage, string> _compactFormat;

    /// <summary>
    ///     Held for the whole write/flush critical section, so disposal cannot land inside one.
    ///     Deliberately never disposed itself: it is only ever awaited (no wait handle is allocated, so
    ///     there is nothing to release), and disposing it is what would make a racing waiter undefined —
    ///     reintroducing this finding one level down.
    /// </summary>
    private readonly SemaphoreSlim _writeGate = new(1, 1);

    private readonly TextWriter _writer;

    private int _disposeRequested;
    private int _writerReleased;

    public StreamCaptureWriter(TextWriter writer, Func<IMessage, string> compactFormat)
    {
        _writer = writer;
        _compactFormat = compactFormat;
    }

    /// <summary>Bytes written so far (for the live capture-size readout).</summary>
    public long BytesWritten { get; private set; }

    /// <summary>
    ///     Writes one event, or does nothing once disposal has been requested. Dropping capture lines
    ///     for a tab the user is closing is the intended trade; throwing at the pump is not.
    /// </summary>
    public async Task WriteAsync(StreamEventModel ev)
    {
        if (Volatile.Read(ref _disposeRequested) != 0)
        {
            return;
        }

        await _writeGate.WaitAsync().ConfigureAwait(false);

        try
        {
            // Re-checked under the gate: the cheap check above only avoids the wait, it decides nothing.
            if (Volatile.Read(ref _disposeRequested) != 0)
            {
                return;
            }

            var line = NdjsonStreamFormatter.Format(ev, _compactFormat);

            await _writer.WriteLineAsync(line).ConfigureAwait(false);
            await _writer.FlushAsync().ConfigureAwait(false);

            BytesWritten += Encoding.UTF8.GetByteCount(line) + Environment.NewLine.Length;
        }
        finally
        {
            _ = _writeGate.Release();

            // A Dispose that arrived while this write held the gate left the writer to us.
            ReleaseWriterIfIdle();
        }
    }

    /// <summary>
    ///     Requests disposal. Idempotent — the owning tab and the capture toggle can both reach it — and
    ///     non-blocking: if a write holds the gate, that write closes the writer on its way out.
    /// </summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposeRequested, 1) != 0)
        {
            return;
        }

        ReleaseWriterIfIdle();
    }

    /// <summary>
    ///     Closes the underlying writer once disposal has been requested and no write holds the gate.
    ///     Called from both sides of the race; whichever arrives second does the work, and the
    ///     <see cref="Interlocked" /> guard means it happens exactly once.
    /// </summary>
    private void ReleaseWriterIfIdle()
    {
        if (Volatile.Read(ref _disposeRequested) == 0 || !_writeGate.Wait(0))
        {
            return;
        }

        try
        {
            if (Interlocked.Exchange(ref _writerReleased, 1) == 0)
            {
                _writer.Dispose();
            }
        }
        finally
        {
            _ = _writeGate.Release();
        }
    }
}
