using Gql2Grpc.Response;
using System.Diagnostics;

namespace Gql2Grpc.Execution;

/// <summary>
///     Runs per-root-field work in parallel with bounded concurrency while preserving document order
///     in the final result array. Concurrency caps at four parallel fields to keep upstream pressure
///     reasonable; tune via <see cref="DefaultMaxDegreeOfParallelism" /> if needed.
/// </summary>
public static class ParallelFieldScheduler
{
    /// <summary>Default upper bound on how many root fields may execute in parallel.</summary>
    public const int DefaultMaxDegreeOfParallelism = 4;

    /// <summary>
    ///     Runs <paramref name="worker" /> over each item in <paramref name="contexts" /> with bounded
    ///     parallelism, returning results in the same order as the input.
    /// </summary>
    /// <typeparam name="TContext">Per-field context shape passed to <paramref name="worker" />.</typeparam>
    /// <param name="contexts">Per-root-field input contexts.</param>
    /// <param name="worker">Async function producing one <see cref="RootFieldResult" /> per context.</param>
    /// <param name="cancellationToken">Cancels in-flight workers on request.</param>
    /// <param name="progress">
    ///     Optional observational sink for per-field <see cref="FieldExecutionProgress" /> transitions
    ///     (Queued → InFlight → Done|Failed). Purely presentational — passing it never changes results
    ///     or ordering. Reports may arrive concurrently from worker threads, so the sink must be
    ///     thread-safe (e.g. one that marshals onto a UI dispatcher).
    /// </param>
    /// <param name="responseKey">
    ///     Projects a context to the response key used to label its progress events; required only when
    ///     <paramref name="progress" /> is supplied.
    /// </param>
    public static async Task<IReadOnlyList<RootFieldResult>> RunAsync<TContext>(
        IReadOnlyList<TContext> contexts,
        Func<TContext, CancellationToken, Task<RootFieldResult>> worker,
        CancellationToken cancellationToken,
        IProgress<FieldExecutionProgress>? progress = null,
        Func<TContext, string>? responseKey = null)
    {
        if (progress is not null && responseKey is null)
        {
            throw new ArgumentNullException(nameof(responseKey), "A response-key selector is required when a progress sink is supplied.");
        }

        if (contexts.Count == 0)
        {
            return [];
        }

        // Announce every field as queued in document order before the bounded window starts draining,
        // so the UI can render all rows up front and then watch them light up.
        if (progress is not null)
        {
            for (var index = 0; index < contexts.Count; index++)
            {
                progress.Report(new FieldExecutionProgress(index, responseKey!(contexts[index]), FieldExecutionState.Queued));
            }
        }

        if (contexts.Count == 1)
        {
            return [await RunOneTracked(0, contexts[0], cancellationToken).ConfigureAwait(false)];
        }

        var results = new RootFieldResult?[contexts.Count];

        var options = new ParallelOptions
        {
            CancellationToken = cancellationToken,
            MaxDegreeOfParallelism = Math.Min(DefaultMaxDegreeOfParallelism, contexts.Count)
        };

        await Parallel.ForEachAsync(
            Enumerable.Range(0, contexts.Count),
            options,
            async (index, ct) => { results[index] = await RunOneTracked(index, contexts[index], ct).ConfigureAwait(false); }).ConfigureAwait(false);

        return [.. results.Cast<RootFieldResult>()];

        async Task<RootFieldResult> RunOneTracked(int index, TContext context, CancellationToken ct)
        {
            if (progress is null)
            {
                return await worker(context, ct).ConfigureAwait(false);
            }

            var key = responseKey!(context);
            progress.Report(new FieldExecutionProgress(index, key, FieldExecutionState.InFlight));

            var stopwatch = Stopwatch.StartNew();
            var result = await worker(context, ct).ConfigureAwait(false);
            stopwatch.Stop();

            var state = result.Failed ? FieldExecutionState.Failed : FieldExecutionState.Done;
            progress.Report(new FieldExecutionProgress(index, key, state, stopwatch.Elapsed));

            return result;
        }
    }
}