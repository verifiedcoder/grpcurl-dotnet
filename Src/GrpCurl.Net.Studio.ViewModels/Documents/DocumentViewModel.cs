using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GrpCurl.Net.Studio.ViewModels.Models.Connections;

namespace GrpCurl.Net.Studio.ViewModels.Documents;

public abstract partial class DocumentViewModel : ViewModelBase
{
    /// <summary>Fire-and-forget work this tab has started. See <see cref="BackgroundWorkSet" />.</summary>
    private readonly BackgroundWorkSet _work = new();

    [ObservableProperty]
    public partial string Title { get; set; } = string.Empty;

    public virtual string DisplayTitle => Title;

    public virtual SavedConnection? TabConnection => null;

    partial void OnTitleChanged(string value) => OnPropertyChanged(nameof(DisplayTitle));

    public event EventHandler? CloseRequested;

    [RelayCommand]
    private void Close() => CloseRequested?.Invoke(this, EventArgs.Empty);

    /// <summary>
    ///     Remembers a task this tab started without awaiting, so shutdown can wait for it. Every
    ///     <c>_ = SomethingAsync()</c> in a document goes through here.
    /// </summary>
    private protected void Track(Task task) => _work.Track(task);

    /// <summary>
    ///     Requests cancellation of whatever this tab <em>can</em> cancel. Called synchronously, before
    ///     anything is awaited.
    ///     <para>
    ///         Cancellation stays selective — some work (a settings refresh, a history load) has no
    ///         token to cancel — but draining does not: uncancellable work is still awaited, under the
    ///         caller's single global timeout. Excluding it is what made the previous round's
    ///         <c>Drained</c> flag factually wrong.
    ///     </para>
    /// </summary>
    protected virtual void CancelOwnedWork()
    {
    }

    /// <summary>
    ///     Adds outstanding work that belongs to a child view model rather than to this tab directly.
    ///     Called synchronously from <see cref="CancelAndDrainAsync" />, so an implementation may cancel
    ///     the child here too.
    /// </summary>
    protected virtual void CollectChildWork(List<Task?> tasks)
    {
    }

    /// <summary>
    ///     Cancels what this tab can cancel and returns a task that completes when <b>all</b> of its
    ///     outstanding work has settled — tracked fire-and-forget tasks, every async command's execution
    ///     task, and any child view model's work alike.
    ///     <para>
    ///         Cancellation is issued synchronously, before the returned task is created, so a caller
    ///         can walk every open tab and be certain all of them have been cancelled before it awaits
    ///         any one of them. Awaiting tab by tab without that guarantee would serialise the drain:
    ///         the last tab would not even be told to stop until the first had finished.
    ///     </para>
    ///     The returned task never faults.
    /// </summary>
    public Task CancelAndDrainAsync()
    {
        CancelOwnedWork();

        var outstanding = AsyncCommandTasks.Of(this);

        CollectChildWork(outstanding);

        return _work.WhenSettled(outstanding);
    }
}
