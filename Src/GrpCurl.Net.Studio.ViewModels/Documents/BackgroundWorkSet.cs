namespace GrpCurl.Net.Studio.ViewModels.Documents;

/// <summary>
///     The outstanding fire-and-forget work an object has started, so shutdown can wait for it
///     (PRD-005 re-review round 3, finding 1).
///     <para>
///         Round 2 drained only the toolkit-generated commands of the three tabs that implemented a
///         drain interface. That set is not the set of work: debounced validation, schema resolution,
///         constructor refreshes, superseded describe loads and every task on the two tabs outside the
///         interface were all invisible to it — so shutdown could report "drained" and dispose the
///         container while those tasks were still using its singletons. Tracking is now unconditional,
///         at the point each task is started, which is the only place that cannot be forgotten by a
///         later reader.
///     </para>
///     <para>
///         <b>Superseded work is retained.</b> A debounce that replaces its predecessor cancels it but
///         does not wait for it; keeping only the newest reference is what let a cancelled-but-still-
///         running load disappear from the drain.
///     </para>
/// </summary>
internal sealed class BackgroundWorkSet
{
    private readonly System.Threading.Lock _gate = new();

    private readonly List<Task> _tasks = [];

    /// <summary>
    ///     Remembers a task until it settles. Completed tasks are pruned on every call, so a tab that
    ///     re-validates on each keystroke does not accumulate them.
    /// </summary>
    public void Track(Task? task)
    {
        if (task is null || task.IsCompleted)
        {
            return;
        }

        lock (_gate)
        {
            _ = _tasks.RemoveAll(t => t.IsCompleted);

            _tasks.Add(task);
        }
    }

    /// <summary>
    ///     A task that completes when everything currently outstanding has finished, however it
    ///     finishes. Never faults: a drain observes that work has stopped, not what it returned, and
    ///     the running code's own error handling has already had its turn.
    /// </summary>
    public Task WhenSettled(IReadOnlyList<Task?>? additional = null)
    {
        List<Task> live;

        lock (_gate)
        {
            _ = _tasks.RemoveAll(t => t.IsCompleted);

            live = [.. _tasks];
        }

        if (additional is not null)
        {
            foreach (var task in additional)
            {
                if (task is { IsCompleted: false })
                {
                    live.Add(task);
                }
            }
        }

        return live.Count == 0 ? Task.CompletedTask : Task.WhenAll(live.Select(Settled));
    }

    private static async Task Settled(Task task)
    {
        try
        {
            await task.ConfigureAwait(false);
        }
        catch (Exception)
        {
            // Deliberately swallowed — see the summary on WhenSettled.
        }
    }
}
