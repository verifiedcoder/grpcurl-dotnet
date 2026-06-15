using GrpCurl.Net.Studio.ViewModels.Services;

namespace GrpCurl.Net.Studio.Tests.Unit.Fakes;

/// <summary>
///     Test <see cref="IUiDispatcher" /> that runs everything inline on the calling thread, so
///     view models can be exercised without a UI thread (SPEC-070).
/// </summary>
public sealed class ImmediateUiDispatcher : IUiDispatcher
{
    public bool IsOnUiThread => true;

    public void Post(Action action) => action();

    public Task InvokeAsync(Action action)
    {
        action();
        return Task.CompletedTask;
    }

    public Task<T> InvokeAsync<T>(Func<T> func) => Task.FromResult(func());
}
