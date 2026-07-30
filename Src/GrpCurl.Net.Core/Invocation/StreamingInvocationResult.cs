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
///         that runs it. Disposal cancels the producer and waits a bounded grace for it to unwind
///         before releasing the call (PRD-003). A producer that honours cancellation is therefore
///         always finished before the call is released. One that does not — an OS read already in
///         flight cannot be recalled — is deliberately left behind rather than hanging the caller:
///         the call is released anyway, its fault is observed so it cannot escape unobserved, and
///         the producer absorbs the resulting <see cref="ObjectDisposedException" />.
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
    DuplexRequestProducer? producer = null) : IAsyncDisposable
{
    private int _disposed;

    public Task<Metadata> ResponseHeadersAsync { get; } = headers;

    public IAsyncEnumerable<IMessage> ResponseStream { get; } = responseStream;

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        if (producer is not null)
        {
            // The fault, if any, is discarded rather than rethrown: whoever enumerated the response
            // stream has already been given the call's outcome, and disposal must not throw a second
            // error at a caller that is only cleaning up. Draining is bounded, so a producer parked
            // in an operation that ignores cancellation cannot hold up teardown.
            producer.OnResponseEnded();

            _ = await producer.DrainAsync(DynamicInvoker.WriterDrainGrace).ConfigureAwait(false);
        }

        dispose();

        // Releases the producer's token sources now if it has stopped, or when it eventually does —
        // a source whose token a parked producer still holds cannot be disposed safely yet.
        producer?.ReleaseWhenIdle();
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
