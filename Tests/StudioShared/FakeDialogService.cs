using GrpCurl.Net.Studio.ViewModels;
using GrpCurl.Net.Studio.ViewModels.Services;

namespace GrpCurl.Net.Studio.TestSupport;

/// <summary>
///     Scriptable <see cref="IDialogService" /> for view-model tests. <see cref="OnShowDialog" />
///     receives the dialog view model and returns the result the user would have chosen;
///     <see cref="ConfirmResult" /> drives confirm prompts.
/// </summary>
public sealed class FakeDialogService : IDialogService
{
    /// <summary>Invoked for each <see cref="ShowDialogAsync{TResult}" />; returns the dialog result (boxed).</summary>
    public Func<object, object?>? OnShowDialog { get; set; }

    public bool ConfirmResult { get; set; }

    public int ConfirmCount { get; private set; }

    public Task ShowMessageAsync(string title, string message, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public Task<bool> ConfirmAsync(string title, string message, CancellationToken cancellationToken = default)
    {
        ConfirmCount++;
        return Task.FromResult(ConfirmResult);
    }

    public Task<TResult?> ShowDialogAsync<TResult>(DialogViewModel<TResult> dialogViewModel)
    {
        var result = OnShowDialog?.Invoke(dialogViewModel);
        return Task.FromResult(result is TResult typed ? typed : default);
    }
}
