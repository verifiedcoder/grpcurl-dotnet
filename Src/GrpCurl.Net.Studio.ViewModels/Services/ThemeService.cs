using CommunityToolkit.Mvvm.ComponentModel;
using GrpCurl.Net.Studio.ViewModels.Models;

namespace GrpCurl.Net.Studio.ViewModels.Services;

/// <summary>
///     Default <see cref="IThemeService" />: holds the current theme (seeded from the persisted
///     settings) and writes changes back through <see cref="ISettingsStore" />. UI-free — the
///     app-layer <c>ThemeManager</c> applies <see cref="Current" /> to Avalonia.
/// </summary>
public sealed partial class ThemeService : ObservableObject, IThemeService
{
    private readonly ISettingsStore _settings;

    [ObservableProperty]
    private AppTheme _current;

    public ThemeService(ISettingsStore settings)
    {
        _settings = settings;
        _current = Parse(settings.Current.Appearance.Theme);
    }

    public async Task SetAsync(AppTheme theme, CancellationToken cancellationToken = default)
    {
        Current = theme;

        var settings = _settings.Current;
        settings.Appearance.Theme = theme.ToString().ToLowerInvariant();
        await _settings.SaveAsync(settings, cancellationToken).ConfigureAwait(false);
    }

    public static AppTheme Parse(string value) => value.ToLowerInvariant() switch
    {
        "light" => AppTheme.Light,
        "dark" => AppTheme.Dark,
        _ => AppTheme.System
    };
}
