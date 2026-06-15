using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GrpCurl.Net.Studio.ViewModels.Models;
using GrpCurl.Net.Studio.ViewModels.Services;

namespace GrpCurl.Net.Studio.ViewModels.Documents;

/// <summary>
///     The Settings tab (FR-150..159). App-scoped settings that persist immediately on change (no
///     Apply button) and survive restarts. Each setting has a per-setting "reset to default"
///     affordance. Theme routes through the shared <see cref="IThemeService" /> (live switch); other
///     settings are written straight back through <see cref="ISettingsStore" />. General + Editor are
///     active here; Network / protoc / the disabled placeholder categories arrive in later E1.6 PRs.
/// </summary>
public sealed partial class SettingsDocumentViewModel : DocumentViewModel
{
    private readonly ISettingsStore _settings;
    private readonly IThemeService _themeService;
    private readonly bool _loaded;

    [ObservableProperty]
    private AppTheme _theme;

    [ObservableProperty]
    private StartupBehavior _startup;

    [ObservableProperty]
    private ShellDialect _cliShellDialect;

    [ObservableProperty]
    private string _editorFontFamily = string.Empty;

    [ObservableProperty]
    private double _editorFontSize;

    [ObservableProperty]
    private int _editorIndentWidth;

    [ObservableProperty]
    private bool _editorFormatOnPaste;

    public SettingsDocumentViewModel(ISettingsStore settings, IThemeService themeService)
    {
        _settings = settings;
        _themeService = themeService;
        Title = "Settings";

        var current = settings.Current;
        _theme = themeService.Current;
        _startup = current.General.Startup;
        _cliShellDialect = current.General.CliShellDialect;
        _editorFontFamily = current.Editor.FontFamily;
        _editorFontSize = current.Editor.FontSize;
        _editorIndentWidth = current.Editor.IndentWidth;
        _editorFormatOnPaste = current.Editor.FormatOnPaste;

        // Keep the theme selector in sync when changed elsewhere (the View menu).
        themeService.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(IThemeService.Current))
            {
                Theme = _themeService.Current;
            }
        };

        _loaded = true;
    }

    public IReadOnlyList<AppTheme> ThemeOptions { get; } = Enum.GetValues<AppTheme>();
    public IReadOnlyList<StartupBehavior> StartupOptions { get; } = Enum.GetValues<StartupBehavior>();
    public IReadOnlyList<ShellDialect> DialectOptions { get; } = Enum.GetValues<ShellDialect>();

    partial void OnThemeChanged(AppTheme value)
    {
        if (!_loaded || value == _themeService.Current)
        {
            return; // initial load, or an echo of a change the service already applied
        }

        _ = _themeService.SetAsync(value);
    }

    partial void OnStartupChanged(StartupBehavior value) => Persist(s => s.General.Startup = value);
    partial void OnCliShellDialectChanged(ShellDialect value) => Persist(s => s.General.CliShellDialect = value);
    partial void OnEditorFontFamilyChanged(string value) => Persist(s => s.Editor.FontFamily = value);
    partial void OnEditorFontSizeChanged(double value) => Persist(s => s.Editor.FontSize = value);
    partial void OnEditorIndentWidthChanged(int value) => Persist(s => s.Editor.IndentWidth = value);
    partial void OnEditorFormatOnPasteChanged(bool value) => Persist(s => s.Editor.FormatOnPaste = value);

    /// <summary>FR-150: per-setting reset to its built-in default. Setting the property re-persists.</summary>
    [RelayCommand]
    private void ResetSetting(string? key)
    {
        var defaults = StudioSettings.Defaults();

        switch (key)
        {
            case "theme": Theme = ThemeService.Parse(defaults.Appearance.Theme); break;
            case "startup": Startup = defaults.General.Startup; break;
            case "dialect": CliShellDialect = defaults.General.CliShellDialect; break;
            case "fontFamily": EditorFontFamily = defaults.Editor.FontFamily; break;
            case "fontSize": EditorFontSize = defaults.Editor.FontSize; break;
            case "indent": EditorIndentWidth = defaults.Editor.IndentWidth; break;
            case "formatOnPaste": EditorFormatOnPaste = defaults.Editor.FormatOnPaste; break;
        }
    }

    private void Persist(Action<StudioSettings> mutate)
    {
        if (!_loaded)
        {
            return;
        }

        var settings = _settings.Current;
        mutate(settings);
        _ = _settings.SaveAsync(settings);
    }
}
