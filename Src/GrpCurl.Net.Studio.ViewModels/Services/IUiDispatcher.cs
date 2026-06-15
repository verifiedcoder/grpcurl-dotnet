namespace GrpCurl.Net.Studio.ViewModels.Services;

/// <summary>
///     Marshals work onto the UI thread. View models depend on this abstraction rather than
///     touching Avalonia's <c>Dispatcher</c> directly, so they remain testable without a UI
///     thread (SPEC-030 §3/§5). The real implementation wraps the Avalonia dispatcher; tests
///     inject an immediate (inline) implementation.
/// </summary>
public interface IUiDispatcher
{
    /// <summary><see langword="true" /> when the caller is already on the UI thread.</summary>
    bool IsOnUiThread { get; }

    /// <summary>Queues <paramref name="action" /> to run on the UI thread without awaiting it.</summary>
    void Post(Action action);

    /// <summary>Runs <paramref name="action" /> on the UI thread and completes when it has run.</summary>
    Task InvokeAsync(Action action);

    /// <summary>Runs <paramref name="func" /> on the UI thread and returns its result.</summary>
    Task<T> InvokeAsync<T>(Func<T> func);
}
