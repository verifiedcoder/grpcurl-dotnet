using Gql2Grpc.Response;

namespace Gql2Grpc.Execution;

/// <summary>
/// Runs per-root-field work in parallel with bounded concurrency while preserving document order
/// in the final result array. Concurrency caps at four parallel fields to keep upstream pressure
/// reasonable; tune via <see cref="DefaultMaxDegreeOfParallelism"/> if needed.
/// </summary>
public static class ParallelFieldScheduler
{
    /// <summary>Default upper bound on how many root fields may execute in parallel.</summary>
    public const int DefaultMaxDegreeOfParallelism = 4;

    /// <summary>
    /// Runs <paramref name="worker"/> over each item in <paramref name="contexts"/> with bounded
    /// parallelism, returning results in the same order as the input.
    /// </summary>
    /// <typeparam name="TContext">Per-field context shape passed to <paramref name="worker"/>.</typeparam>
    /// <param name="contexts">Per-root-field input contexts.</param>
    /// <param name="worker">Async function producing one <see cref="RootFieldResult"/> per context.</param>
    /// <param name="cancellationToken">Cancels in-flight workers on request.</param>
    public static async Task<IReadOnlyList<RootFieldResult>> RunAsync<TContext>(
        IReadOnlyList<TContext> contexts,
        Func<TContext, CancellationToken, Task<RootFieldResult>> worker,
        CancellationToken cancellationToken)
    {
        if (contexts.Count == 0)
        {
            return Array.Empty<RootFieldResult>();
        }

        if (contexts.Count == 1)
        {
            return new[] { await worker(contexts[0], cancellationToken).ConfigureAwait(false) };
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
            async (index, ct) =>
            {
                results[index] = await worker(contexts[index], ct).ConfigureAwait(false);
            }).ConfigureAwait(false);

        return results.Cast<RootFieldResult>().ToList();
    }
}
