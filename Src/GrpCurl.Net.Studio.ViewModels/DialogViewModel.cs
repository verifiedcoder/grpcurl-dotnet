namespace GrpCurl.Net.Studio.ViewModels;

/// <summary>
///     Base for view models hosted in a modal dialog. The view model requests closure with a
///     result via <see cref="Close" />; the dialog service observes <see cref="CloseRequested" />
///     to dismiss the window and return the result. Keeps dialog logic headless-testable
///     independent of any window.
/// </summary>
public abstract class DialogViewModel<TResult> : ViewModelBase
{
    /// <summary>Raised when the view model asks to close, carrying the dialog result.</summary>
    public event Action<TResult?>? CloseRequested;

    /// <summary>The dialog's window-chrome title. Empty leaves the host window untitled; concrete dialogs
    /// override it so the title shows in the OS window bar rather than as a duplicated in-body header.</summary>
    public virtual string Title => string.Empty;

    protected void Close(TResult? result) => CloseRequested?.Invoke(result);
}
