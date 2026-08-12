using static System.FormattableString;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GrpCurl.Net.Studio.ViewModels.Models;
using GrpCurl.Net.Studio.ViewModels.Models.Diagnostics;
using GrpCurl.Net.Studio.ViewModels.Services;
using System.ComponentModel;

namespace GrpCurl.Net.Studio.ViewModels.Documents;

/// <summary>
///     The Settings tab (FR-150..159). App-scoped settings that persist immediately on change (no
///     Apply button) and survive restarts. Each setting has a per-setting "reset to default"
///     affordance, plus a "reset all" (FR-159). Theme routes through the shared
///     <see cref="IThemeService" /> (live switch); other settings are written straight back through
///     <see cref="ISettingsStore" />. All categories are active: General, Editor, Network, protoc,
///     Security, History, Descriptor limits, Updates, and Diagnostics (FR-155 log viewer).
/// </summary>
public sealed partial class SettingsDocumentViewModel : DocumentViewModel, IDisposable
{
    private bool _disposed;

    private readonly ISettingsStore _settings;
    private readonly IThemeService _themeService;
    private readonly IDialogService _dialogs;
    private readonly IProtocService? _protoc;
    private readonly IUpdateService? _updates;
    private readonly ILauncherService? _launcher;
    private readonly IDiagnosticsLog? _diagnostics;
    private readonly IClipboardService? _clipboard;
    private readonly List<DiagnosticsLogEntry> _allDiagnostics = [];
    private readonly ISecretStore? _secrets;
    private readonly SecretStoreInfo? _secretInfo;
    private readonly bool _loaded;
    private bool _applying;

    [ObservableProperty]
    public partial AppTheme Theme { get; set; }

    [ObservableProperty]
    public partial StartupBehavior Startup { get; set; }

    [ObservableProperty]
    public partial ShellDialect CliShellDialect { get; set; }

    [ObservableProperty]
    public partial string EditorFontFamily { get; set; } = string.Empty;

    [ObservableProperty]
    public partial double EditorFontSize { get; set; }

    [ObservableProperty]
    public partial int EditorIndentWidth { get; set; }

    [ObservableProperty]
    public partial bool EditorFormatOnPaste { get; set; }

    [ObservableProperty]
    public partial string NetworkConnectTimeout { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string NetworkKeepaliveTime { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string NetworkKeepaliveTimeout { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string NetworkMaxMessageSize { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string NetworkDefaultDeadline { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string ProtocPath { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string? ProtocStatus { get; set; }

    // ── History (FR-158) ─────────────────────────────────────────────────────

    [ObservableProperty]
    public partial bool HistoryCaptureEnabled { get; set; }

    [ObservableProperty]
    public partial bool HistoryCaptureResponses { get; set; }

    [ObservableProperty]
    public partial int HistoryMaxEntries { get; set; }

    [ObservableProperty]
    public partial int HistoryMaxSizeMiB { get; set; }

    [ObservableProperty]
    public partial int HistoryResponseCapKiB { get; set; }

    // ── Descriptor limits (FR-157) ───────────────────────────────────────────

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DescriptorMaxProtosetMiBChanged))]
    public partial int DescriptorMaxProtosetMiB { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DescriptorMaxReflectionMiBChanged))]
    public partial int DescriptorMaxReflectionMiB { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DescriptorMaxFileDescriptorsChanged))]
    public partial int DescriptorMaxFileDescriptors { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DescriptorMaxDependencyDepthChanged))]
    public partial int DescriptorMaxDependencyDepth { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DescriptorMaxSymbolsChanged))]
    public partial int DescriptorMaxSymbols { get; set; }

    // ── Updates (FR-156) ─────────────────────────────────────────────────────

    [ObservableProperty]
    public partial UpdateChannel UpdateChannel { get; set; }

    [ObservableProperty]
    public partial bool UpdateCheckOnLaunch { get; set; }

    [ObservableProperty]
    public partial string? UpdateStatus { get; set; }

    /// <summary>FR-156: set when the last check found a newer release; drives the "Open release" affordance.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(OpenLatestReleaseCommand))]
    public partial bool UpdateAvailable { get; set; }

    [ObservableProperty]
    public partial string? LatestVersion { get; set; }

    private string? _latestReleaseUrl;

    // ── Diagnostics (FR-155) ─────────────────────────────────────────────────

    [ObservableProperty]
    public partial DiagnosticsLevel DiagnosticsLevelFilter { get; set; } = DiagnosticsLevel.Information;

    [ObservableProperty]
    public partial string DiagnosticsSearch { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string? DiagnosticsStatus { get; set; }

    public SettingsDocumentViewModel(
        ISettingsStore settings,
        IThemeService themeService,
        IDialogService dialogs,
        IProtocService? protoc = null,
        ISecretStore? secrets = null,
        IUpdateService? updates = null,
        ILauncherService? launcher = null,
        IDiagnosticsLog? diagnostics = null,
        IClipboardService? clipboard = null)
    {
        _settings = settings;
        _themeService = themeService;
        _dialogs = dialogs;
        _protoc = protoc;
        _updates = updates;
        _launcher = launcher;
        _diagnostics = diagnostics;
        _clipboard = clipboard;
        _secrets = secrets;
        _secretInfo = secrets?.Info;
        Title = "Settings";

        if (_diagnostics is not null)
        {
            _ = RefreshDiagnosticsAsync();
        }

        if (_secrets is not null)
        {
            _ = RefreshSecretsAsync(); // SEC-027: populate the audit list
        }

        LoadFrom(settings.Current, themeService.Current);

        // Keep the theme selector in sync when changed elsewhere (the View menu). A named handler
        // rather than a lambda so Dispose can unhook it: the theme service is a container singleton,
        // and an anonymous handler would root every closed Settings tab for the life of the process
        // with no reference left to remove (PRD-005).
        themeService.PropertyChanged += OnThemeServiceChanged;

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

    /// <summary>SEC-027: the keyrefs the secret store holds (names only, never values), for audit + cleanup.</summary>
    public System.Collections.ObjectModel.ObservableCollection<string> SecretKeyRefs { get; } = [];

    public bool HasNoSecretKeyRefs => SecretKeyRefs.Count == 0;

    /// <summary>SEC-027: reloads the stored-keyref list from the secret store.</summary>
    [RelayCommand]
    private async Task RefreshSecrets() => await RefreshSecretsAsync();

    private async Task RefreshSecretsAsync()
    {
        if (_secrets is null)
        {
            return;
        }

        var keyRefs = await _secrets.ListAsync();
        SecretKeyRefs.Clear();

        foreach (var keyRef in keyRefs)
        {
            SecretKeyRefs.Add(keyRef);
        }

        OnPropertyChanged(nameof(HasNoSecretKeyRefs));
    }

    /// <summary>SEC-027: permanently delete one stored secret by keyref (after confirmation), then refresh.</summary>
    [RelayCommand]
    private async Task DeleteSecret(string? keyRef)
    {
        if (_secrets is null || string.IsNullOrEmpty(keyRef))
        {
            return;
        }

        if (!await _dialogs.ConfirmAsync(
                "Delete secret?",
                $"Permanently delete the stored secret '{keyRef}'? Any connection or environment referencing it will need the value re-entered."))
        {
            return;
        }

        await _secrets.DeleteAsync(keyRef);
        await RefreshSecretsAsync();
    }

    public IReadOnlyList<AppTheme> ThemeOptions { get; } = Enum.GetValues<AppTheme>();
    public IReadOnlyList<StartupBehavior> StartupOptions { get; } = Enum.GetValues<StartupBehavior>();
    public IReadOnlyList<ShellDialect> DialectOptions { get; } = Enum.GetValues<ShellDialect>();
    public IReadOnlyList<UpdateChannel> UpdateChannelOptions { get; } = Enum.GetValues<UpdateChannel>();

    /// <summary>FR-155: the filtered diagnostics entries shown in the viewer (oldest first).</summary>
    public System.Collections.ObjectModel.ObservableCollection<DiagnosticsLogEntry> DiagnosticsEntries { get; } = [];

    public IReadOnlyList<DiagnosticsLevel> DiagnosticsLevelOptions { get; } = Enum.GetValues<DiagnosticsLevel>();

    /// <summary>Whether the diagnostics log is wired (its actions are available).</summary>
    public bool HasDiagnostics => _diagnostics is not null;

    public bool HasNoDiagnosticsEntries => DiagnosticsEntries.Count == 0;

    /// <summary>FR-156: the running application version (or a dash when the update service isn't wired).</summary>
    public string AppVersion => _updates?.CurrentVersion ?? "—";

    /// <summary>Whether the Updates actions are available (the update + launcher services are wired).</summary>
    public bool CanCheckForUpdates => _updates is not null && _launcher is not null;

    // ── Descriptor limits: Core defaults (FR-157 "showing the Core default") ──

    public static int DescriptorDefaultProtosetMiB
        => (int)(GrpCurl.Net.DescriptorSources.DescriptorSourceOptions.DefaultMaxProtosetFileBytes / (1024L * 1024L));

    public static int DescriptorDefaultReflectionMiB
        => (int)(GrpCurl.Net.DescriptorSources.DescriptorSourceOptions.DefaultMaxReflectionDescriptorBytes / (1024L * 1024L));

    public static int DescriptorDefaultFileDescriptors
        => GrpCurl.Net.DescriptorSources.DescriptorSourceOptions.DefaultMaxFileDescriptors;

    public static int DescriptorDefaultDependencyDepth
        => GrpCurl.Net.DescriptorSources.DescriptorSourceOptions.DefaultMaxDependencyDepth;

    public static int DescriptorDefaultSymbols
        => GrpCurl.Net.DescriptorSources.DescriptorSourceOptions.DefaultMaxSymbols;

    public bool DescriptorMaxProtosetMiBChanged => DescriptorMaxProtosetMiB != DescriptorDefaultProtosetMiB;
    public bool DescriptorMaxReflectionMiBChanged => DescriptorMaxReflectionMiB != DescriptorDefaultReflectionMiB;
    public bool DescriptorMaxFileDescriptorsChanged => DescriptorMaxFileDescriptors != DescriptorDefaultFileDescriptors;
    public bool DescriptorMaxDependencyDepthChanged => DescriptorMaxDependencyDepth != DescriptorDefaultDependencyDepth;
    public bool DescriptorMaxSymbolsChanged => DescriptorMaxSymbols != DescriptorDefaultSymbols;

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
    partial void OnHistoryCaptureEnabledChanged(bool value) => Persist(s => s.History.Enabled = value);
    partial void OnHistoryCaptureResponsesChanged(bool value) => Persist(s => s.History.CaptureResponses = value);
    partial void OnHistoryMaxEntriesChanged(int value) => Persist(s => s.History.MaxEntries = Math.Max(1, value));
    partial void OnHistoryMaxSizeMiBChanged(int value) => Persist(s => s.History.MaxBytes = Math.Max(1, value) * 1024L * 1024L);
    partial void OnHistoryResponseCapKiBChanged(int value) => Persist(s => s.History.ResponseCapBytes = Math.Max(1, value) * 1024);
    partial void OnDescriptorMaxProtosetMiBChanged(int value) => Persist(s => s.DescriptorLimits.MaxProtosetFileBytes = Math.Max(1, value) * 1024L * 1024L);
    partial void OnDescriptorMaxReflectionMiBChanged(int value) => Persist(s => s.DescriptorLimits.MaxReflectionDescriptorBytes = Math.Max(1, value) * 1024L * 1024L);
    partial void OnDescriptorMaxFileDescriptorsChanged(int value) => Persist(s => s.DescriptorLimits.MaxFileDescriptors = Math.Max(1, value));
    partial void OnDescriptorMaxDependencyDepthChanged(int value) => Persist(s => s.DescriptorLimits.MaxDependencyDepth = Math.Max(1, value));
    partial void OnDescriptorMaxSymbolsChanged(int value) => Persist(s => s.DescriptorLimits.MaxSymbols = Math.Max(1, value));
    partial void OnUpdateChannelChanged(UpdateChannel value) => Persist(s => s.Updates.Channel = value);
    partial void OnUpdateCheckOnLaunchChanged(bool value) => Persist(s => s.Updates.CheckOnLaunch = value);

    /// <summary>
    ///     FR-156: a manual, consent-respecting check — compares the running version against the channel's latest
    ///     release. On an available update it offers to open that release; if the check fails (offline), it falls
    ///     back to opening the releases page so the user can look manually. Nothing is ever downloaded (ADR-011).
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanCheckForUpdates))]
    private async Task CheckForUpdates()
    {
        if (_updates is null || _launcher is null)
        {
            return;
        }

        UpdateStatus = "Checking for updates…";
        var result = await _updates.CheckForUpdateAsync(UpdateChannel);

        switch (result.Availability)
        {
            case UpdateAvailability.UpdateAvailable:
                UpdateAvailable = true;
                LatestVersion = result.LatestVersion;
                _latestReleaseUrl = result.ReleaseUrl;
                UpdateStatus = $"Update available: {result.LatestVersion} (you have {_updates.CurrentVersion}).";
                break;

            case UpdateAvailability.UpToDate:
                UpdateAvailable = false;
                LatestVersion = null;
                _latestReleaseUrl = null;
                UpdateStatus = $"You're on the latest version ({_updates.CurrentVersion}).";
                break;

            default: // CheckFailed — offline or unreachable; let the user check manually.
                UpdateAvailable = false;
                UpdateStatus = "Couldn't check for updates (offline or GitHub unreachable). Opening the releases page…";
                _ = await _launcher.LaunchUriAsync(_updates.ReleasesUrl(UpdateChannel));
                break;
        }
    }

    private bool CanOpenLatestRelease => UpdateAvailable && _launcher is not null;

    /// <summary>FR-156: open the page for the release the last check found (consent-respecting, no auto-download).</summary>
    [RelayCommand(CanExecute = nameof(CanOpenLatestRelease))]
    private async Task OpenLatestRelease()
    {
        if (_launcher is not null)
        {
            _ = await _launcher.LaunchUriAsync(_latestReleaseUrl ?? _updates?.ReleasesUrl(UpdateChannel) ?? string.Empty);
        }
    }

    partial void OnDiagnosticsLevelFilterChanged(DiagnosticsLevel value) => ApplyDiagnosticsFilter();
    partial void OnDiagnosticsSearchChanged(string value) => ApplyDiagnosticsFilter();

    /// <summary>FR-155: reloads the diagnostics entries from the log file and re-applies the filter.</summary>
    [RelayCommand]
    private async Task RefreshDiagnostics() => await RefreshDiagnosticsAsync();

    private async Task RefreshDiagnosticsAsync()
    {
        if (_diagnostics is null)
        {
            return;
        }

        var entries = await _diagnostics.ReadRecentAsync();
        _allDiagnostics.Clear();
        _allDiagnostics.AddRange(entries);
        ApplyDiagnosticsFilter();
    }

    private void ApplyDiagnosticsFilter()
    {
        var search = DiagnosticsSearch.Trim();

        DiagnosticsEntries.Clear();

        foreach (var entry in _allDiagnostics.Where(e =>
                     e.Level >= DiagnosticsLevelFilter
                     && (search.Length == 0
                         || e.Message.Contains(search, StringComparison.OrdinalIgnoreCase)
                         || e.Category.Contains(search, StringComparison.OrdinalIgnoreCase))))
        {
            DiagnosticsEntries.Add(entry);
        }

        OnPropertyChanged(nameof(HasNoDiagnosticsEntries));
    }

    /// <summary>FR-155: open the folder holding the diagnostics log file.</summary>
    [RelayCommand(CanExecute = nameof(HasDiagnostics))]
    private async Task OpenLogFolder()
    {
        if (_diagnostics is null || _launcher is null)
        {
            return;
        }

        _ = await _launcher.LaunchUriAsync(new Uri(_diagnostics.LogFolderPath).AbsoluteUri);
    }

    /// <summary>
    ///     FR-155: copy a diagnostics bundle — the logs plus app version and OS — to the clipboard. Contains
    ///     no workspace content and no secrets (entries carry header names only, SEC-031).
    /// </summary>
    [RelayCommand(CanExecute = nameof(HasDiagnostics))]
    private async Task CopyDiagnosticsBundle()
    {
        if (_clipboard is null)
        {
            return;
        }

        var bundle = new System.Text.StringBuilder();
        _ = bundle.AppendLine("GrpCurl.Net Studio diagnostics");
        _ = bundle.AppendLine(Invariant($"Version: {_updates?.CurrentVersion ?? "unknown"}"));
        _ = bundle.AppendLine(Invariant($"OS: {System.Runtime.InteropServices.RuntimeInformation.OSDescription}"));
        _ = bundle.AppendLine();

        foreach (var entry in _allDiagnostics)
        {
            _ = bundle.AppendLine(Invariant($"{entry.At:u} [{entry.Level}] {entry.Category}: {entry.Message}"));
        }

        await _clipboard.SetTextAsync(bundle.ToString());
        DiagnosticsStatus = "Copied the diagnostics bundle to the clipboard.";
    }

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
            case "historyMaxEntries": HistoryMaxEntries = d.History.MaxEntries; break;
            case "historyMaxSize": HistoryMaxSizeMiB = (int)(d.History.MaxBytes / (1024L * 1024L)); break;
            case "historyResponseCap": HistoryResponseCapKiB = d.History.ResponseCapBytes / 1024; break;
            case "descriptorProtoset": DescriptorMaxProtosetMiB = DescriptorDefaultProtosetMiB; break;
            case "descriptorReflection": DescriptorMaxReflectionMiB = DescriptorDefaultReflectionMiB; break;
            case "descriptorFiles": DescriptorMaxFileDescriptors = DescriptorDefaultFileDescriptors; break;
            case "descriptorDepth": DescriptorMaxDependencyDepth = DescriptorDefaultDependencyDepth; break;
            case "descriptorSymbols": DescriptorMaxSymbols = DescriptorDefaultSymbols; break;
            case "updateChannel": UpdateChannel = d.Updates.Channel; break;
            case "updateCheckOnLaunch": UpdateCheckOnLaunch = d.Updates.CheckOnLaunch; break;
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
        HistoryCaptureEnabled = s.History.Enabled;
        HistoryCaptureResponses = s.History.CaptureResponses;
        HistoryMaxEntries = s.History.MaxEntries;
        HistoryMaxSizeMiB = (int)Math.Max(1, s.History.MaxBytes / (1024L * 1024L));
        HistoryResponseCapKiB = Math.Max(1, s.History.ResponseCapBytes / 1024);
        DescriptorMaxProtosetMiB = (int)Math.Max(1, s.DescriptorLimits.MaxProtosetFileBytes / (1024L * 1024L));
        DescriptorMaxReflectionMiB = (int)Math.Max(1, s.DescriptorLimits.MaxReflectionDescriptorBytes / (1024L * 1024L));
        DescriptorMaxFileDescriptors = s.DescriptorLimits.MaxFileDescriptors;
        DescriptorMaxDependencyDepth = s.DescriptorLimits.MaxDependencyDepth;
        DescriptorMaxSymbols = s.DescriptorLimits.MaxSymbols;
        UpdateChannel = s.Updates.Channel;
        UpdateCheckOnLaunch = s.Updates.CheckOnLaunch;
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

    private void OnThemeServiceChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(IThemeService.Current))
        {
            Theme = _themeService.Current;
        }
    }

    /// <summary>
    ///     Unhooks the theme service when the tab closes (PRD-005). Idempotent and non-throwing.
    ///     <para>
    ///         <see cref="IThemeService" /> is a container singleton, so this subscription is the one
    ///         thing here that outlives the tab: without it every closed Settings tab stayed reachable
    ///         from the singleton for the life of the process.
    ///     </para>
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        _themeService.PropertyChanged -= OnThemeServiceChanged;
    }
}
