using System.Threading.Channels;

namespace GrpCurl.Net.Studio.ViewModels.Services;

/// <summary>
///     Batches a streaming event sequence for the UI (ADR-013): a producer drains the source into a
///     bounded queue; a consumer flushes everything currently available in one <paramref name="apply" />
///     call, so a flood collapses into a few large batches (≥30 fps) while a trickle stays responsive.
///     The queue is bounded with <see cref="BoundedChannelFullMode.Wait" /> so a hot server stream the UI
///     cannot render fast enough backpressures the producer — which stops pulling from the source, which
///     backpressures <c>InvocationService</c>'s own bounded channel into HTTP/2 flow control rather than
///     growing memory without bound. The <paramref name="apply" /> callback marshals onto the UI thread
///     (via <c>IUiDispatcher</c>); this type stays UI-framework-free and deterministic for headless tests.
///     Cancellation surfaces after every already-queued event has been applied (cancel-preserves-received,
///     FR-084).
/// </summary>
public static class StreamDispatchPump
{
    /// <summary>
    ///     Default UI-queue capacity. Matches <c>InvocationService</c>'s source channel so the two stages
    ///     hold a comparable backlog before backpressure propagates to the wire.
    /// </summary>
    public const int DefaultCapacity = 1000;

    public static async Task RunAsync<T>(
        IAsyncEnumerable<T> source,
        Func<IReadOnlyList<T>, Task> apply,
        CancellationToken cancellationToken,
        int capacity = DefaultCapacity)
    {
        var queue = Channel.CreateBounded<T>(new BoundedChannelOptions(capacity)
        {
            SingleReader = true,
            FullMode = BoundedChannelFullMode.Wait
        });

        var producer = Task.Run(async () =>
        {
            try
            {
                await foreach (var item in source.WithCancellation(cancellationToken).ConfigureAwait(false))
                {
                    await queue.Writer.WriteAsync(item, cancellationToken).ConfigureAwait(false);
                }
            }
            finally
            {
                _ = queue.Writer.TryComplete();
            }
        }, cancellationToken);

        // No token on the reader: drain to channel completion so queued events are applied even as
        // cancellation tears the producer down. The producer's exception is surfaced afterwards.
        while (await queue.Reader.WaitToReadAsync(CancellationToken.None).ConfigureAwait(false))
        {
            var batch = new List<T>();
            while (queue.Reader.TryRead(out var item))
            {
                batch.Add(item);
            }

            if (batch.Count > 0)
            {
                await apply(batch).ConfigureAwait(false);
            }
        }

        await producer.ConfigureAwait(false);
    }
}
