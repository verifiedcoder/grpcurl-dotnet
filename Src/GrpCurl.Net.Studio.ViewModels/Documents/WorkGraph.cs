namespace GrpCurl.Net.Studio.ViewModels.Documents;

/// <summary>
///     Implemented by a view model that owns other view models with async work of their own
///     (PRD-005 re-review round 4, finding 2).
///     <para>
///         Reflection finds the async commands declared on <em>one</em> object. A tab is not one object:
///         an invocation owns a stream log whose rows have copy commands, and response metadata rows
///         whose reveal command awaits the singleton <c>IRevealGate</c>. Those tasks are not properties
///         of the tab, so the query cannot reach them and shutdown could not wait for them. This
///         interface is the explicit ownership link the query cannot infer.
///     </para>
/// </summary>
internal interface IOwnsBackgroundWork
{
    /// <summary>
    ///     Adds the outstanding work of everything this object owns, via
    ///     <see cref="WorkGraph.Collect" /> so that grandchildren are reached too. Called synchronously
    ///     during the drain's cancel phase, so an implementation may cancel its children here.
    /// </summary>
    void CollectOwnedWork(List<Task?> tasks);
}

/// <summary>
///     Walks an object and everything it owns, collecting outstanding async work (PRD-005).
/// </summary>
internal static class WorkGraph
{
    /// <summary>
    ///     Adds <paramref name="node" />'s own async-command tasks, then recurses through
    ///     <see cref="IOwnsBackgroundWork" /> into whatever it owns. A node that owns nothing needs no
    ///     interface and no registration — only the commands it declares itself are collected.
    /// </summary>
    public static void Collect(object node, List<Task?> tasks)
    {
        tasks.AddRange(AsyncCommandTasks.Of(node));

        if (node is IOwnsBackgroundWork owner)
        {
            owner.CollectOwnedWork(tasks);
        }
    }

    /// <summary>Collects every element of a child collection — the common case for row view models.</summary>
    public static void CollectAll<T>(IEnumerable<T> children, List<Task?> tasks)
        where T : notnull
    {
        foreach (var child in children)
        {
            Collect(child, tasks);
        }
    }

    /// <summary>
    ///     Moves a departing child's outstanding work into <paramref name="owner" />'s set, so removal
    ///     does not discard the only handle on it (PRD-005 re-review round 5, finding 2).
    ///     <para>
    ///         Collecting children by walking the live collections describes what is <em>reachable</em>,
    ///         not what is <em>owned</em>. A stream log is a ring buffer: at capacity it evicts its
    ///         oldest row, and if that row's copy command was still awaiting the singleton clipboard,
    ///         the task carried on somewhere nothing could see it. This is the same hand-off
    ///         <c>DocumentsViewModel.Retire</c> performs for a closed tab, one level down.
    ///     </para>
    /// </summary>
    public static void Retain(BackgroundWorkSet owner, object child)
    {
        var tasks = new List<Task?>();

        Collect(child, tasks);

        foreach (var task in tasks)
        {
            owner.Track(task);
        }
    }

    /// <summary>Retains every child about to leave a collection — the <c>Clear</c> case.</summary>
    public static void RetainAll<T>(BackgroundWorkSet owner, IEnumerable<T> children)
        where T : notnull
    {
        foreach (var child in children)
        {
            Retain(owner, child);
        }
    }
}
