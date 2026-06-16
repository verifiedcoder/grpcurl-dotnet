using System.Threading.Channels;

namespace GrpCurl.Net.Studio.ViewModels.Services;

/// <summary>
///     Batches a streaming event sequence for the UI (ADR-013): a producer drains the source into an
///     unbounded queue; a consumer flushes everything currently available in one <paramref name="apply" />
///     call, so a flood collapses into a few large batches (≥30 fps) while a trickle stays responsive.
///     The <paramref name="apply" /> callback marshals onto the UI thread (via <c>IUiDispatcher</c>);
///     this type stays UI-framework-free and deterministic for headless tests. Cancellation surfaces
///     after every already-queued event has been applied (cancel-preserves-received, FR-084).
/// </summary>
public sealed class StreamDispatchPump
{
    public async Task RunAsync<T>(
        IAsyncEnumerable<T> source,
        Func<IReadOnlyList<T>, Task> apply,
        CancellationToken cancellationToken)
    {
        var queue = Channel.CreateUnbounded<T>(new UnboundedChannelOptions { SingleReader = true });

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
                queue.Writer.TryComplete();
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
