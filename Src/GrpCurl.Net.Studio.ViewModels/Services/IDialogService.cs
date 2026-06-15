namespace GrpCurl.Net.Studio.ViewModels.Services;

/// <summary>
///     Presents modal prompts to the user. Abstracted so view models can be unit-tested with
///     a scripted fake instead of real windows (SPEC-030 §4).
/// </summary>
public interface IDialogService
{
    /// <summary>Shows an informational message and returns when dismissed.</summary>
    Task ShowMessageAsync(string title, string message, CancellationToken cancellationToken = default);

    /// <summary>Asks a yes/no question; returns <see langword="true" /> for yes.</summary>
    Task<bool> ConfirmAsync(string title, string message, CancellationToken cancellationToken = default);
}
