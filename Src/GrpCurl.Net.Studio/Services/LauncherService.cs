using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using GrpCurl.Net.Studio.ViewModels.Services;

namespace GrpCurl.Net.Studio.Services;

/// <summary>Opens URIs via the active window's <see cref="TopLevel.Launcher" /> (FR-094).</summary>
internal sealed class LauncherService : ILauncherService
{
    public async Task<bool> LaunchUriAsync(string uri, CancellationToken cancellationToken = default)
    {
        if (TopLevel() is { Launcher: { } launcher } && Uri.TryCreate(uri, UriKind.Absolute, out var parsed))
        {
            return await launcher.LaunchUriAsync(parsed);
        }

        return false;
    }

    private static TopLevel? TopLevel()
        => (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
}
