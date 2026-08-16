namespace GrpCurl.Net.Studio.ViewModels.Documents;

/// <summary>
///     The outstanding fire-and-forget work an object has started, so shutdown can wait for it
///     (PRD-005 re-review rounds 3 and 4).
///     <para>
///         Round 2 drained only the toolkit-generated commands of three tab types. That set is not the
///         set of work: debounced validation, schema resolution, constructor refreshes, superseded
///         describe loads and everything on the two tabs outside that list were all invisible to it, so
///         shutdown could report "drained" while those tasks were still using container singletons.
///         Tracking is therefore unconditional and happens at the point each task is started, which is
///         the only place a later reader cannot forget.
///     </para>
///     <para>
///         <b>Superseded work is retained.</b> A debounce that replaces its predecessor cancels it but
///         does not wait for it; keeping only the newest reference is what let a cancelled-but-still-
///         running load disappear from the drain.
///     </para>
///     <para>
///         <b>The wait is for quiescence, not for a snapshot.</b> An earlier version copied the live
///         tasks once and waited on that copy, so work registered by an <em>already admitted</em> task
///         was missed — a validation started from the callback of a dialog the drain was waiting on, for
///         instance (round 4, finding 3). A pending count fixes that by construction: the parent is
///         still counted while it registers its successor, so the count cannot reach zero in between.
///     </para>
/// </summary>
internal sealed class BackgroundWorkSet
{
    private readonly System.Threading.Lock _gate = new();

    private int _pending;

    private TaskCompletionSource? _quiescent;

    /// <summary>
    ///     Counts a task until it finishes. Already-completed tasks are ignored: anything they started
    ///     was registered while they ran, so it is already counted.
    /// </summary>
    public void Track(Task? task)
    {
        if (task is null)
        {
            return;
        }

        if (task.IsCompleted)
        {
            Observe(task);

            return;
        }

        lock (_gate)
        {
            _pending++;
        }

        // ExecuteSynchronously so the count drops on the completing thread, before anything that
        // awaited the task can run and conclude the set is empty.
        _ = task.ContinueWith(
            Retire, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    }

    /// <summary>
    ///     A task that completes when nothing is outstanding — including work registered after this
    ///     call by work that was already running. Never faults.
    /// </summary>
    /// <param name="additional">
    ///     Tasks to enrol first, for work that is not started through <see cref="Track" /> — a
    ///     generated command's execution task, or a child view model's drain.
    /// </param>
    public Task WhenSettled(IReadOnlyList<Task?>? additional = null)
    {
        if (additional is not null)
        {
            foreach (var task in additional)
            {
                Track(task);
            }
        }

        lock (_gate)
        {
            return _pending == 0
                ? Task.CompletedTask
                : (_quiescent ??= new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously)).Task;
        }
    }

    private void Retire(Task task)
    {
        Observe(task);

        TaskCompletionSource? quiescent = null;

        lock (_gate)
        {
            if (--_pending == 0)
            {
                quiescent = _quiescent;
                _quiescent = null;
            }
        }

        _ = quiescent?.TrySetResult();
    }

    /// <summary>
    ///     Marks a faulted task's exception observed. A drain watches for work stopping, not for what it
    ///     returned — the running code's own error handling has already had its turn — but leaving the
    ///     exception unobserved would surface it on the finaliser thread instead.
    /// </summary>
    private static void Observe(Task task) => _ = task.Exception;
}
