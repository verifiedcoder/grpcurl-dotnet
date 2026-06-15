namespace GrpCurl.Net.Studio.ViewModels.Services;

/// <summary>
///     Reads and writes the system clipboard. Abstracted (rather than using Avalonia's
///     <c>IClipboard</c> directly) so view models avoid a UI dependency (SPEC-030 §1/§4).
/// </summary>
public interface IClipboardService
{
    Task SetTextAsync(string text, CancellationToken cancellationToken = default);

    Task<string?> GetTextAsync(CancellationToken cancellationToken = default);
}
