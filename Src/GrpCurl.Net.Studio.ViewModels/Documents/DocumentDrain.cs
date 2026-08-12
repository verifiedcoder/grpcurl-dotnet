namespace GrpCurl.Net.Studio.ViewModels.Documents;

/// <summary>
///     Helpers for <see cref="IDrainableDocument.CancelAndDrainAsync" /> implementations (PRD-005).
/// </summary>
internal static class DocumentDrain
{
    /// <summary>
    ///     Waits for every task that is still running, ignoring nulls, already-finished tasks, and how
    ///     any of them ended.
    ///     <para>
    ///         A generated command's <c>ExecutionTask</c> is <see langword="null" /> until the command
    ///         first runs, and a cancelled one usually ends faulted or cancelled —
    ///         <see cref="Task.WhenAll(Task[])" /> alone would rethrow that at shutdown, out of a path
    ///         whose whole job is to finish quietly.
    ///     </para>
    /// </summary>
    public static Task WhenSettled(params Task?[] tasks)
    {
        List<Task>? running = null;

        foreach (var task in tasks)
        {
            if (task is { IsCompleted: false })
            {
                (running ??= []).Add(Settled(task));
            }
        }

        return running is null ? Task.CompletedTask : Task.WhenAll(running);
    }

    private static async Task Settled(Task task)
    {
        try
        {
            await task.ConfigureAwait(false);
        }
        catch (Exception)
        {
            // Deliberately swallowed: this is a drain, not an observation. The operation's own error
            // handling already ran, and the outcome of work cancelled at shutdown changes nothing.
        }
    }
}
