namespace GrpCurl.Net.Studio.ViewModels.Documents;

/// <summary>
///     A tab that owns background work application shutdown must <em>stop</em>, not merely ask to stop
///     (PRD-005 re-review, finding 1).
///     <para>
///         Cancelling a toolkit-generated command sets its token; the operation keeps running until it
///         observes that and unwinds, and on the way out it still dispatches state to the UI and can
///         record history. Shutdown that only cancels therefore races <c>host.Dispose()</c>, which tears
///         down the very singletons — history, validation, secrets — that unwinding work reaches for.
///     </para>
/// </summary>
public interface IDrainableDocument
{
    /// <summary>
    ///     Requests cancellation of everything this tab started and returns a task that completes when
    ///     that work has unwound.
    ///     <para>
    ///         Cancellation is issued synchronously, before the returned task is created, so a caller can
    ///         walk every open tab and be certain all of them have been cancelled before it awaits any
    ///         one of them. Awaiting tab by tab without that guarantee would serialise the drain: the
    ///         last tab would not even be told to stop until the first had finished.
    ///     </para>
    ///     The task never faults. How cancelled work ended is the running code's business; shutdown only
    ///     needs to know that it is no longer running.
    /// </summary>
    Task CancelAndDrainAsync();
}
