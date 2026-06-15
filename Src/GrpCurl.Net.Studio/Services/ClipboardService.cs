using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input.Platform;
using GrpCurl.Net.Studio.ViewModels.Services;

namespace GrpCurl.Net.Studio.Services;

/// <summary>
///     Real <see cref="IClipboardService" /> backed by the main window's platform clipboard.
///     No-ops gracefully if there is no desktop top level (e.g. headless), so callers never throw.
/// </summary>
internal sealed class ClipboardService : IClipboardService
{
    public async Task SetTextAsync(string text, CancellationToken cancellationToken = default)
    {
        if (Clipboard() is { } clipboard)
        {
            await clipboard.SetTextAsync(text);
        }
    }

    public async Task<string?> GetTextAsync(CancellationToken cancellationToken = default)
        => Clipboard() is { } clipboard ? await clipboard.TryGetTextAsync() : null;

    private static IClipboard? Clipboard()
        => (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow?.Clipboard;
}
