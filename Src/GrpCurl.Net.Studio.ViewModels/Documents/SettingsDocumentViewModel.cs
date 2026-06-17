using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GrpCurl.Net.Studio.ViewModels.Models;
using GrpCurl.Net.Studio.ViewModels.Services;

namespace GrpCurl.Net.Studio.ViewModels.Documents;

/// <summary>
///     The Settings tab (FR-150..159). App-scoped settings that persist immediately on change (no
///     Apply button) and survive restarts. Each setting has a per-setting "reset to default"
///     affordance, plus a "reset all" (FR-159). Theme routes through the shared
///     <see cref="IThemeService" /> (live switch); other settings are written straight back through
///     <see cref="ISettingsStore" />. General / Editor / Network / protoc are active; Diagnostics,
///     Updates, Descriptor limits, and History render as disabled placeholders (Phase 2).
/// </summary>
public sealed partial class SettingsDocumentViewModel : DocumentViewModel
{
    private readonly ISettingsStore _settings;
    private readonly IThemeService _themeService;
    private readonly IDialogService _dialogs;
    private readonly IProtocService? _protoc;
    private readonly SecretStoreInfo? _secretInfo;
    private readonly bool _loaded;
    private bool _applying;

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

    [ObservableProperty]
    private string _networkConnectTimeout = string.Empty;

    [ObservableProperty]
    private string _networkKeepaliveTime = string.Empty;

    [ObservableProperty]
    private string _networkKeepaliveTimeout = string.Empty;

    [ObservableProperty]
    private string _networkMaxMessageSize = string.Empty;

    [ObservableProperty]
    private string _networkDefaultDeadline = string.Empty;

    [ObservableProperty]
    private string _protocPath = string.Empty;

    [ObservableProperty]
    private string? _protocStatus;

    public SettingsDocumentViewModel(
        ISettingsStore settings,
        IThemeService themeService,
        IDialogService dialogs,
        IProtocService? protoc = null,
        ISecretStore? secrets = null)
    {
        _settings = settings;
        _themeService = themeService;
        _dialogs = dialogs;
        _protoc = protoc;
        _secretInfo = secrets?.Info;
        Title = "Settings";

        LoadFrom(settings.Current, themeService.Current);

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

    // ── Security (SEC-024): read-only view of the live secret-store backend ──────

    /// <summary>Whether the secret-store backend is known (wired in the real app; null in bare constructions).</summary>
    public bool HasSecretBackend => _secretInfo is not null;

    /// <summary>The live backend name, e.g. "macOS Keychain" or "Encrypted file (fallback)".</summary>
    public string SecretBackendName => _secretInfo?.BackendName ?? "Unknown";

    /// <summary>True when secrets are held in an OS keychain; false for the encrypted-file fallback.</summary>
    public bool SecretBackendIsOsKeychain => _secretInfo?.IsOsKeychain ?? false;

    /// <summary>True for the degraded encrypted-file fallback, which carries a limitation note.</summary>
    public bool SecretBackendHasLimitation => _secretInfo?.LimitationNote is not null;

    /// <summary>The verbatim honest-limitation text for the fallback backend (SEC-024), or null.</summary>
    public string? SecretBackendLimitation => _secretInfo?.LimitationNote;

    public IReadOnlyList<AppTheme> ThemeOptions { get; } = Enum.GetValues<AppTheme>();
    public IReadOnlyList<StartupBehavior> StartupOptions { get; } = Enum.GetValues<StartupBehavior>();
    public IReadOnlyList<ShellDialect> DialectOptions { get; } = Enum.GetValues<ShellDialect>();

    partial void OnThemeChanged(AppTheme value)
    {
        if (!_loaded || _applying || value == _themeService.Current)
        {
            return; // initial load / a reset-all batch / an echo of a change the service already applied
        }

        _ = _themeService.SetAsync(value);
    }

    partial void OnStartupChanged(StartupBehavior value) => Persist(s => s.General.Startup = value);
    partial void OnCliShellDialectChanged(ShellDialect value) => Persist(s => s.General.CliShellDialect = value);
    partial void OnEditorFontFamilyChanged(string value) => Persist(s => s.Editor.FontFamily = value);
    partial void OnEditorFontSizeChanged(double value) => Persist(s => s.Editor.FontSize = value);
    partial void OnEditorIndentWidthChanged(int value) => Persist(s => s.Editor.IndentWidth = value);
    partial void OnEditorFormatOnPasteChanged(bool value) => Persist(s => s.Editor.FormatOnPaste = value);
    partial void OnNetworkConnectTimeoutChanged(string value) => Persist(s => s.Network.ConnectTimeout = value);
    partial void OnNetworkKeepaliveTimeChanged(string value) => Persist(s => s.Network.KeepaliveTime = value);
    partial void OnNetworkKeepaliveTimeoutChanged(string value) => Persist(s => s.Network.KeepaliveTimeout = value);
    partial void OnNetworkMaxMessageSizeChanged(string value) => Persist(s => s.Network.MaxMessageSize = value);
    partial void OnNetworkDefaultDeadlineChanged(string value) => Persist(s => s.Network.DefaultDeadline = value);
    partial void OnProtocPathChanged(string value) => Persist(s => s.Protoc.Path = value);

    /// <summary>FR-150: per-setting reset to its built-in default. Setting the property re-persists.</summary>
    [RelayCommand]
    private void ResetSetting(string? key)
    {
        var d = StudioSettings.Defaults();

        switch (key)
        {
            case "theme": Theme = ThemeService.Parse(d.Appearance.Theme); break;
            case "startup": Startup = d.General.Startup; break;
            case "dialect": CliShellDialect = d.General.CliShellDialect; break;
            case "fontFamily": EditorFontFamily = d.Editor.FontFamily; break;
            case "fontSize": EditorFontSize = d.Editor.FontSize; break;
            case "indent": EditorIndentWidth = d.Editor.IndentWidth; break;
            case "formatOnPaste": EditorFormatOnPaste = d.Editor.FormatOnPaste; break;
            case "connectTimeout": NetworkConnectTimeout = d.Network.ConnectTimeout; break;
            case "keepaliveTime": NetworkKeepaliveTime = d.Network.KeepaliveTime; break;
            case "keepaliveTimeout": NetworkKeepaliveTimeout = d.Network.KeepaliveTimeout; break;
            case "maxMessageSize": NetworkMaxMessageSize = d.Network.MaxMessageSize; break;
            case "defaultDeadline": NetworkDefaultDeadline = d.Network.DefaultDeadline; break;
            case "protocPath": ProtocPath = d.Protoc.Path; break;
        }
    }

    /// <summary>FR-154: report what a PATH lookup currently resolves.</summary>
    [RelayCommand]
    private async Task DetectProtoc()
    {
        if (_protoc is null)
        {
            return;
        }

        ProtocStatus = "Detecting…";
        ProtocStatus = (await _protoc.DetectAsync()).Message;
    }

    /// <summary>FR-154: verify the override path by running <c>--version</c>.</summary>
    [RelayCommand]
    private async Task VerifyProtoc()
    {
        if (_protoc is null)
        {
            return;
        }

        ProtocStatus = "Verifying…";
        ProtocStatus = (await _protoc.VerifyAsync(ProtocPath)).Message;
    }

    /// <summary>FR-159: reset every setting to its default (after confirmation). Workspaces/secrets untouched.</summary>
    [RelayCommand]
    private async Task ResetAll()
    {
        var confirmed = await _dialogs.ConfirmAsync(
            "Reset all settings",
            "Reset every setting to its default? This does not affect your workspaces, history, or secrets.");

        if (!confirmed)
        {
            return;
        }

        var d = StudioSettings.Defaults();

        _applying = true;
        LoadFrom(d, ThemeService.Parse(d.Appearance.Theme));
        ProtocStatus = null;
        _applying = false;

        await _settings.SaveAsync(d);
        await _themeService.SetAsync(ThemeService.Parse(d.Appearance.Theme));
    }

    private void LoadFrom(StudioSettings s, AppTheme theme)
    {
        Theme = theme;
        Startup = s.General.Startup;
        CliShellDialect = s.General.CliShellDialect;
        EditorFontFamily = s.Editor.FontFamily;
        EditorFontSize = s.Editor.FontSize;
        EditorIndentWidth = s.Editor.IndentWidth;
        EditorFormatOnPaste = s.Editor.FormatOnPaste;
        NetworkConnectTimeout = s.Network.ConnectTimeout;
        NetworkKeepaliveTime = s.Network.KeepaliveTime;
        NetworkKeepaliveTimeout = s.Network.KeepaliveTimeout;
        NetworkMaxMessageSize = s.Network.MaxMessageSize;
        NetworkDefaultDeadline = s.Network.DefaultDeadline;
        ProtocPath = s.Protoc.Path;
    }

    private void Persist(Action<StudioSettings> mutate)
    {
        if (!_loaded || _applying)
        {
            return;
        }

        var settings = _settings.Current;
        mutate(settings);
        _ = _settings.SaveAsync(settings);
    }
}
