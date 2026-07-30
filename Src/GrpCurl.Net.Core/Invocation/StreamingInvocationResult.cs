using Google.Protobuf;
using Grpc.Core;

namespace GrpCurl.Net.Invocation;

/// <summary>
///     Wraps a streaming gRPC call so callers can access the response stream alongside
///     the headers (resolved early) and trailers (resolved after the stream completes).
///     Used for server-streaming, client-streaming, and bidi-streaming so verbose output
///     can emit headers/trailers uniformly with the unary case (CODE-REVIEW.md P2
///     "Response Headers/Trailers Parity").
///     <para>
///         For bidi-streaming the result also owns the request producer's lifetime: the
///         linked <paramref name="writerCts" /> that can stop it and the <paramref name="writerTask" />
///         that runs it. Disposal cancels the producer and waits for it before releasing the
///         call, so the call is never torn down underneath an in-flight write (PRD-003).
///         Server-streaming passes neither, and disposal degenerates to releasing the call.
///     </para>
/// </summary>
/// <remarks>
///     Deliberately <see cref="IAsyncDisposable" /> and NOT <see cref="IDisposable" />: releasing
///     the call correctly requires awaiting the producer, which a synchronous Dispose cannot do.
///     Omitting <see cref="IDisposable" /> makes the compiler reject <c>using</c> at every call
///     site, so no consumer can silently take a path that disposes the call while the producer
///     is still running.
/// </remarks>
internal sealed class StreamingInvocationResult(
    Task<Metadata> headers,
    IAsyncEnumerable<IMessage> responseStream,
    Func<Metadata?> trailersAccessor,
    Action dispose,
    CancellationTokenSource? writerCts = null,
    Task? writerTask = null) : IAsyncDisposable
{
    private int _disposed;

    public Task<Metadata> ResponseHeadersAsync { get; } = headers;

    public IAsyncEnumerable<IMessage> ResponseStream { get; } = responseStream;

    /// <summary>
    ///     Cancels a writer token source that a concurrent teardown may already have disposed.
    ///     <see cref="CancellationTokenSource.Cancel()" /> throws <see cref="ObjectDisposedException" />
    ///     after disposal and <see cref="CancellationTokenSource.CancelAsync" /> returns a faulted
    ///     task, and <c>CancellationTokenSource.Dispose</c> is documented as not thread-safe against
    ///     concurrent calls. <c>CancelAsync</c> is used rather than <c>Cancel</c> so grpc-dotnet's
    ///     cancellation callback does not run inline on the response reader's thread.
    /// </summary>
    internal static async ValueTask CancelQuietlyAsync(CancellationTokenSource? cts)
    {
        if (cts is null)
        {
            return;
        }

        try
        {
            await cts.CancelAsync().ConfigureAwait(false);
        }
        catch (ObjectDisposedException)
        {
            // Teardown already ran; the producer is stopping or has stopped.
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        await CancelQuietlyAsync(writerCts).ConfigureAwait(false);

        if (writerTask is not null)
        {
            try
            {
                await writerTask.WaitAsync(DynamicInvoker.WriterDrainGrace).ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
                // The producer is parked in an operation that does not honour cancellation
                // (an interactive stdin read is the canonical case). Observe any later fault
                // so it cannot escape unobserved, then release the call anyway — blocking on
                // the caller's request source is exactly the hang PRD-003 fixes.
                DynamicInvoker.ObserveFault(writerTask);
            }
            catch
            {
                // Observed. The response side is authoritative for the call's outcome, and the
                // consumer has already seen it; a producer fault here would be a duplicate.
            }
        }

        dispose();

        // Only safe once nothing can still be holding the token: a parked producer would
        // otherwise observe a disposed source. The linked registration is released with the
        // caller's own token source.
        if (writerTask is null or { IsCompleted: true })
        {
            writerCts?.Dispose();
        }
    }

    /// <summary>
    ///     Returns the trailers once the response stream has completed. Returns
    ///     <see langword="null" /> if accessed before completion or if trailers are unavailable.
    /// </summary>
    public Metadata? GetTrailers()
    {
        try
        {
            return trailersAccessor();
        }
        catch
        {
            return null;
        }
    }
}
