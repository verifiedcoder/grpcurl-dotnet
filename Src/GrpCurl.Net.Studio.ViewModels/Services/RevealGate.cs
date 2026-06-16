namespace GrpCurl.Net.Studio.ViewModels.Services;

/// <summary>
///     Default <see cref="IRevealGate" />: shows the secret-reveal warning once per app run via
///     <see cref="IDialogService" />. Singleton so the acknowledgement is shared across all tabs and
///     fields (FR-113).
/// </summary>
public sealed class RevealGate(IDialogService dialog) : IRevealGate
{
    private bool _acknowledged;

    public async Task<bool> ConfirmRevealAsync()
    {
        if (_acknowledged)
        {
            return true;
        }

        var proceed = await dialog.ConfirmAsync(
            "Reveal value?",
            "This value will be visible on screen — beware screen sharing and recordings. "
            + "Reveal it? (You won't be warned again this session.)");

        if (proceed)
        {
            _acknowledged = true;
        }

        return proceed;
    }
}
