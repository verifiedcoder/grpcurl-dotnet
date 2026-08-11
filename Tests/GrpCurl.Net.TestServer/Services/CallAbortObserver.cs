using System.Collections.Concurrent;

namespace GrpCurl.Net.TestServer.Services;

/// <summary>
///     Lets a test observe how a server handler unwound, so "the client released the call" can be
///     asserted from the server's side rather than inferred (PRD-004).
///     <para>
///         The observer is deliberately passive: it records an outcome and never completes, cancels
///         or times out anything. A handler parked in <c>ReadAllAsync</c> on a request stream the
///         client abandoned has no way out except the client resetting the stream, so an
///         <see cref="Outcome.Aborted" /> result cannot be produced by the server acting alone.
///     </para>
/// </summary>
public static class CallAbortObserver
{
    private static readonly ConcurrentDictionary<string, TaskCompletionSource<Outcome>> Pending = new();

    public enum Outcome
    {
        /// <summary>The handler read the request stream to its end — the client half-closed.</summary>
        Drained,

        /// <summary>The call was cancelled or its stream reset while the handler was still reading.</summary>
        Aborted,

        /// <summary>The handler threw for some other reason (including its own configured failure).</summary>
        Faulted
    }

    /// <summary>
    ///     Registers <paramref name="id" /> and returns the task completed when a handler carrying that
    ///     id in <see cref="MetadataConstants.ObserveAbortId" /> unwinds. Call before the RPC starts.
    /// </summary>
    public static Task<Outcome> Register(string id)
    {
        var completion = new TaskCompletionSource<Outcome>(TaskCreationOptions.RunContinuationsAsynchronously);

        if (!Pending.TryAdd(id, completion))
        {
            throw new InvalidOperationException($"An observation is already registered for id '{id}'.");
        }

        return completion.Task;
    }

    /// <summary>
    ///     Records how a handler unwound. A no-op when the id is absent or already recorded, so
    ///     handlers can call it unconditionally.
    /// </summary>
    public static void Record(string? id, Outcome outcome)
    {
        if (id is null || !Pending.TryRemove(id, out var completion))
        {
            return;
        }

        _ = completion.TrySetResult(outcome);
    }

    /// <summary>
    ///     Removes a registration a test made but never triggered, so an abandoned id cannot be
    ///     matched by a later test that reuses the value.
    /// </summary>
    public static void Forget(string id) => Pending.TryRemove(id, out _);
}
