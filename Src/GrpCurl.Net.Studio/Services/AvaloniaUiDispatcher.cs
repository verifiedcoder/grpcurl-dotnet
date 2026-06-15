using Avalonia.Threading;
using GrpCurl.Net.Studio.ViewModels.Services;

namespace GrpCurl.Net.Studio.Services;

/// <summary>
///     <see cref="IUiDispatcher" /> backed by Avalonia's UI-thread dispatcher. This is the
///     only place the app marshals to the UI thread; view models depend on the abstraction.
/// </summary>
internal sealed class AvaloniaUiDispatcher : IUiDispatcher
{
    public bool IsOnUiThread => Dispatcher.UIThread.CheckAccess();

    public void Post(Action action) => Dispatcher.UIThread.Post(action);

    public Task InvokeAsync(Action action) => Dispatcher.UIThread.InvokeAsync(action).GetTask();

    public Task<T> InvokeAsync<T>(Func<T> func) => Dispatcher.UIThread.InvokeAsync(func).GetTask();
}
