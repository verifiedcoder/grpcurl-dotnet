using GrpCurl.Net.Studio.Tests.Unit.Fakes;
using GrpCurl.Net.Studio.TestSupport;
using GrpCurl.Net.Studio.ViewModels.Documents;
using GrpCurl.Net.Studio.ViewModels.Models;
using GrpCurl.Net.Studio.ViewModels.Models.Diagnostics;
using GrpCurl.Net.Studio.ViewModels.Services;

namespace GrpCurl.Net.Studio.Tests.Unit;

public sealed class SettingsDocumentViewModelTests
{
    private static SettingsDocumentViewModel Create(out FakeSettingsStore store, out FakeThemeService theme)
        => Create(out store, out theme, out _, out _);

    private static SettingsDocumentViewModel Create(
        out FakeSettingsStore store, out FakeThemeService theme, out FakeDialogService dialogs, out FakeProtocService protoc)
    {
        store = new FakeSettingsStore();
        theme = new FakeThemeService();
        dialogs = new FakeDialogService();
        protoc = new FakeProtocService();
        return new SettingsDocumentViewModel(store, theme, dialogs, protoc);
    }

    // ── Security panel (SEC-024) ─────────────────────────────────────────────

    [Fact]
    public void Without_a_secret_store_the_security_panel_is_hidden()
    {
        var vm = new SettingsDocumentViewModel(new FakeSettingsStore(), new FakeThemeService(), new FakeDialogService());

        vm.HasSecretBackend.ShouldBeFalse();
    }

    [Fact]
    public void The_security_panel_reflects_an_os_keychain_backend()
    {
        var secrets = new FakeSecretStore { Info = new SecretStoreInfo("macOS Keychain", IsOsKeychain: true, LimitationNote: null) };

        var vm = new SettingsDocumentViewModel(
            new FakeSettingsStore(), new FakeThemeService(), new FakeDialogService(), new FakeProtocService(), secrets);

        vm.HasSecretBackend.ShouldBeTrue();
        vm.SecretBackendName.ShouldBe("macOS Keychain");
        vm.SecretBackendIsOsKeychain.ShouldBeTrue();
        vm.SecretBackendHasLimitation.ShouldBeFalse();
        vm.SecretBackendLimitation.ShouldBeNull();
    }

    [Fact]
    public void The_security_panel_surfaces_the_fallback_limitation_verbatim()
    {
        var secrets = new FakeSecretStore
        {
            Info = new SecretStoreInfo("Encrypted file (fallback)", IsOsKeychain: false, LimitationNote: "weaker than a keychain")
        };

        var vm = new SettingsDocumentViewModel(
            new FakeSettingsStore(), new FakeThemeService(), new FakeDialogService(), new FakeProtocService(), secrets);

        vm.SecretBackendIsOsKeychain.ShouldBeFalse();
        vm.SecretBackendHasLimitation.ShouldBeTrue();
        vm.SecretBackendLimitation.ShouldBe("weaker than a keychain");
    }

    [Fact]
    public void Seeds_from_current_settings_without_persisting()
    {
        var store = new FakeSettingsStore();
        store.Current.General.CliShellDialect = ShellDialect.PowerShell;
        store.Current.Editor.FontSize = 16;

        var vm = new SettingsDocumentViewModel(store, new FakeThemeService(), new FakeDialogService(), new FakeProtocService());

        vm.Title.ShouldBe("Settings");
        vm.CliShellDialect.ShouldBe(ShellDialect.PowerShell);
        vm.EditorFontSize.ShouldBe(16);
        store.SaveCount.ShouldBe(0); // loading must not write
    }

    [Fact]
    public void Changing_a_setting_persists_immediately()
    {
        var vm = Create(out var store, out _);

        vm.Startup = StartupBehavior.StartEmpty;

        store.SaveCount.ShouldBe(1);
        store.Current.General.Startup.ShouldBe(StartupBehavior.StartEmpty);
    }

    [Fact]
    public void Editor_setting_change_persists()
    {
        var vm = Create(out var store, out _);

        vm.EditorIndentWidth = 4;

        store.SaveCount.ShouldBe(1);
        store.Current.Editor.IndentWidth.ShouldBe(4);
    }

    [Fact]
    public void Theme_change_routes_through_the_theme_service_not_the_store()
    {
        var vm = Create(out var store, out var theme);

        vm.Theme = AppTheme.Dark;

        theme.SetCount.ShouldBe(1);
        theme.Current.ShouldBe(AppTheme.Dark);
        store.SaveCount.ShouldBe(0); // theme persistence is the service's job
    }

    [Fact]
    public void Reset_restores_a_setting_to_its_default_and_repersists()
    {
        var vm = Create(out var store, out _);
        vm.EditorFontSize = 22;
        store.Current.Editor.FontSize.ShouldBe(22);

        vm.ResetSettingCommand.Execute("fontSize");

        vm.EditorFontSize.ShouldBe(StudioSettings.Defaults().Editor.FontSize);
        store.Current.Editor.FontSize.ShouldBe(StudioSettings.Defaults().Editor.FontSize);
        store.SaveCount.ShouldBe(2); // one set + one reset
    }

    [Fact]
    public void Theme_changed_elsewhere_updates_the_selector()
    {
        var vm = Create(out _, out var theme);

        theme.Current = AppTheme.Light; // e.g. the View menu changed it

        vm.Theme.ShouldBe(AppTheme.Light);
    }

    [Fact]
    public void Network_default_change_persists()
    {
        var vm = Create(out var store, out _);

        vm.NetworkConnectTimeout = "7s";

        store.SaveCount.ShouldBe(1);
        store.Current.Network.ConnectTimeout.ShouldBe("7s");
    }

    [Fact]
    public void Protoc_path_change_persists()
    {
        var vm = Create(out var store, out _);

        vm.ProtocPath = "/opt/protoc/bin/protoc";

        store.SaveCount.ShouldBe(1);
        store.Current.Protoc.Path.ShouldBe("/opt/protoc/bin/protoc");
    }

    [Fact]
    public async Task Detect_protoc_reports_the_probe_result()
    {
        var vm = Create(out _, out _, out _, out var protoc);
        protoc.DetectResult = ProtocInfo.Ok("/usr/bin/protoc", "libprotoc 4.25");

        await vm.DetectProtocCommand.ExecuteAsync(null);

        protoc.DetectCount.ShouldBe(1);
        vm.ProtocStatus!.ShouldContain("libprotoc 4.25");
    }

    [Fact]
    public async Task Verify_protoc_runs_against_the_current_path()
    {
        var vm = Create(out _, out _, out _, out var protoc);
        vm.ProtocPath = "/opt/protoc";
        protoc.VerifyResult = ProtocInfo.NotFound("'/opt/protoc' did not respond to --version.");

        await vm.VerifyProtocCommand.ExecuteAsync(null);

        protoc.VerifyCount.ShouldBe(1);
        protoc.LastVerifiedPath.ShouldBe("/opt/protoc");
        vm.ProtocStatus!.ShouldContain("did not respond");
    }

    [Fact]
    public async Task Reset_all_confirms_then_restores_defaults_and_persists_once()
    {
        var vm = Create(out var store, out var theme, out var dialogs, out _);
        dialogs.ConfirmResult = true;
        vm.NetworkConnectTimeout = "99s";
        vm.EditorFontSize = 22;
        await theme.SetAsync(AppTheme.Dark, TestContext.Current.CancellationToken);
        var savesBefore = store.SaveCount;

        await vm.ResetAllCommand.ExecuteAsync(null);

        dialogs.ConfirmCount.ShouldBe(1);
        var d = StudioSettings.Defaults();
        vm.NetworkConnectTimeout.ShouldBe(d.Network.ConnectTimeout);
        vm.EditorFontSize.ShouldBe(d.Editor.FontSize);
        vm.Theme.ShouldBe(ThemeService.Parse(d.Appearance.Theme));
        store.Current.Network.ConnectTimeout.ShouldBe(d.Network.ConnectTimeout);
        (store.SaveCount - savesBefore).ShouldBe(1); // one batched save (theme persists via the service)
    }

    [Fact]
    public async Task Reset_all_does_nothing_when_declined()
    {
        var vm = Create(out var store, out _, out var dialogs, out _);
        dialogs.ConfirmResult = false;
        vm.NetworkConnectTimeout = "99s";
        var savesBefore = store.SaveCount;

        await vm.ResetAllCommand.ExecuteAsync(null);

        vm.NetworkConnectTimeout.ShouldBe("99s");
        (store.SaveCount - savesBefore).ShouldBe(0);
    }

    // ── Diagnostics (FR-155) ─────────────────────────────────────────────────

    private static SettingsDocumentViewModel WithDiagnostics(
        FakeDiagnosticsLog log, out FakeClipboardService clipboard, out FakeLauncherService launcher)
    {
        clipboard = new FakeClipboardService();
        launcher = new FakeLauncherService();
        return new SettingsDocumentViewModel(
            new FakeSettingsStore(), new FakeThemeService(), new FakeDialogService(), new FakeProtocService(),
            secrets: null, new FakeUpdateService { CurrentVersion = "1.2.3" }, launcher, log, clipboard);
    }

    [Fact]
    public void Diagnostics_entries_load_and_filter_by_level()
    {
        var log = new FakeDiagnosticsLog();
        log.Entries.Add(new(DateTimeOffset.UtcNow, DiagnosticsLevel.Debug, "a", "debug line"));
        log.Entries.Add(new(DateTimeOffset.UtcNow, DiagnosticsLevel.Information, "b", "info line"));
        log.Entries.Add(new(DateTimeOffset.UtcNow, DiagnosticsLevel.Error, "c", "error line"));

        var vm = WithDiagnostics(log, out _, out _);

        // Default filter is Information, so Debug is hidden.
        vm.DiagnosticsEntries.Select(e => e.Message).ShouldBe(["info line", "error line"]);

        vm.DiagnosticsLevelFilter = DiagnosticsLevel.Error;
        vm.DiagnosticsEntries.Select(e => e.Message).ShouldBe(["error line"]);
    }

    [Fact]
    public void Diagnostics_search_filters_by_message_or_category()
    {
        var log = new FakeDiagnosticsLog();
        log.Entries.Add(new(DateTimeOffset.UtcNow, DiagnosticsLevel.Information, "SecretStore", "backend: macOS Keychain"));
        log.Entries.Add(new(DateTimeOffset.UtcNow, DiagnosticsLevel.Information, "Workspace", "opened project.gcnws.json"));
        var vm = WithDiagnostics(log, out _, out _);

        vm.DiagnosticsSearch = "keychain";
        vm.DiagnosticsEntries.ShouldHaveSingleItem().Category.ShouldBe("SecretStore");

        vm.DiagnosticsSearch = "workspace"; // matches the category
        vm.DiagnosticsEntries.ShouldHaveSingleItem().Category.ShouldBe("Workspace");
    }

    [Fact]
    public async Task Copy_diagnostics_bundle_includes_version_os_and_entries()
    {
        var log = new FakeDiagnosticsLog();
        log.Entries.Add(new(DateTimeOffset.UtcNow, DiagnosticsLevel.Warning, "Net", "connect timeout"));
        var vm = WithDiagnostics(log, out var clipboard, out _);

        await vm.CopyDiagnosticsBundleCommand.ExecuteAsync(null);

        var bundle = clipboard.Text.ShouldNotBeNull();
        bundle.ShouldContain("Version: 1.2.3");
        bundle.ShouldContain("OS:");
        bundle.ShouldContain("connect timeout");
        vm.DiagnosticsStatus.ShouldNotBeNull();
    }

    [Fact]
    public async Task Open_log_folder_launches_the_folder()
    {
        var log = new FakeDiagnosticsLog();
        var vm = WithDiagnostics(log, out _, out var launcher);

        await vm.OpenLogFolderCommand.ExecuteAsync(null);

        launcher.LaunchCount.ShouldBe(1);
        launcher.LastUri.ShouldNotBeNull().ShouldStartWith("file:");
    }

    [Fact]
    public void Without_a_diagnostics_log_the_section_is_unavailable()
    {
        var vm = new SettingsDocumentViewModel(new FakeSettingsStore(), new FakeThemeService(), new FakeDialogService());

        vm.HasDiagnostics.ShouldBeFalse();
        vm.OpenLogFolderCommand.CanExecute(null).ShouldBeFalse();
        vm.CopyDiagnosticsBundleCommand.CanExecute(null).ShouldBeFalse();
    }

    // ── Updates (FR-156) ─────────────────────────────────────────────────────

    [Fact]
    public void Update_settings_load_and_expose_the_app_version()
    {
        var store = new FakeSettingsStore();
        store.Current.Updates.Channel = UpdateChannel.Preview;
        store.Current.Updates.CheckOnLaunch = false;
        var updates = new FakeUpdateService { CurrentVersion = "2.3.4" };

        var vm = new SettingsDocumentViewModel(
            store, new FakeThemeService(), new FakeDialogService(), new FakeProtocService(), secrets: null,
            updates, new FakeLauncherService());

        vm.AppVersion.ShouldBe("2.3.4");
        vm.UpdateChannel.ShouldBe(UpdateChannel.Preview);
        vm.UpdateCheckOnLaunch.ShouldBeFalse();
        vm.CanCheckForUpdates.ShouldBeTrue();
    }

    [Fact]
    public void Changing_update_settings_persists()
    {
        var store = new FakeSettingsStore();
        var vm = new SettingsDocumentViewModel(
            store, new FakeThemeService(), new FakeDialogService(), new FakeProtocService(), secrets: null,
            new FakeUpdateService(), new FakeLauncherService());

        vm.UpdateChannel = UpdateChannel.Preview;
        vm.UpdateCheckOnLaunch = false;

        store.Current.Updates.Channel.ShouldBe(UpdateChannel.Preview);
        store.Current.Updates.CheckOnLaunch.ShouldBeFalse();
    }

    private static SettingsDocumentViewModel CreateForUpdates(FakeUpdateService updates, FakeLauncherService launcher)
        => new(new FakeSettingsStore(), new FakeThemeService(), new FakeDialogService(), new FakeProtocService(), secrets: null,
            updates, launcher) { UpdateChannel = UpdateChannel.Stable };

    [Fact]
    public async Task Check_now_reports_an_available_update_without_auto_opening()
    {
        var updates = new FakeUpdateService { CheckResult = UpdateCheckResult.Available("v2.0.0", "https://example.test/v2") };
        var launcher = new FakeLauncherService();
        var vm = CreateForUpdates(updates, launcher);

        await vm.CheckForUpdatesCommand.ExecuteAsync(null);

        vm.UpdateAvailable.ShouldBeTrue();
        vm.LatestVersion.ShouldBe("v2.0.0");
        launcher.LaunchCount.ShouldBe(0); // ADR-011: nothing opened until the user acts

        vm.OpenLatestReleaseCommand.CanExecute(null).ShouldBeTrue();
        await vm.OpenLatestReleaseCommand.ExecuteAsync(null);
        launcher.LastUri.ShouldBe("https://example.test/v2");
    }

    [Fact]
    public async Task Check_now_reports_up_to_date_without_opening_a_page()
    {
        var launcher = new FakeLauncherService();
        var vm = CreateForUpdates(new FakeUpdateService { CheckResult = UpdateCheckResult.UpToDate }, launcher);

        await vm.CheckForUpdatesCommand.ExecuteAsync(null);

        vm.UpdateAvailable.ShouldBeFalse();
        launcher.LaunchCount.ShouldBe(0);
        vm.UpdateStatus.ShouldNotBeNull().ShouldContain("latest");
    }

    [Fact]
    public async Task Check_now_falls_back_to_the_releases_page_when_the_check_fails()
    {
        var launcher = new FakeLauncherService();
        var vm = CreateForUpdates(new FakeUpdateService { CheckResult = UpdateCheckResult.Failed }, launcher);

        await vm.CheckForUpdatesCommand.ExecuteAsync(null);

        launcher.LaunchCount.ShouldBe(1);
        launcher.LastUri.ShouldBe("https://example.test/releases/latest"); // manual fallback
    }

    [Fact]
    public void Without_an_update_service_the_check_is_unavailable()
    {
        var vm = new SettingsDocumentViewModel(new FakeSettingsStore(), new FakeThemeService(), new FakeDialogService());

        vm.CanCheckForUpdates.ShouldBeFalse();
        vm.CheckForUpdatesCommand.CanExecute(null).ShouldBeFalse();
        vm.AppVersion.ShouldBe("—");
    }

    // ── History (FR-158) ─────────────────────────────────────────────────────

    [Fact]
    public void History_settings_load_from_current()
    {
        var store = new FakeSettingsStore();
        store.Current.History.Enabled = false;
        store.Current.History.CaptureResponses = true;
        store.Current.History.MaxEntries = 250;
        store.Current.History.MaxBytes = 8L * 1024 * 1024;
        store.Current.History.ResponseCapBytes = 64 * 1024;

        var vm = new SettingsDocumentViewModel(store, new FakeThemeService(), new FakeDialogService(), new FakeProtocService());

        vm.HistoryCaptureEnabled.ShouldBeFalse();
        vm.HistoryCaptureResponses.ShouldBeTrue();
        vm.HistoryMaxEntries.ShouldBe(250);
        vm.HistoryMaxSizeMiB.ShouldBe(8);
        vm.HistoryResponseCapKiB.ShouldBe(64);
    }

    [Fact]
    public void Changing_history_settings_persists_in_canonical_units()
    {
        var vm = Create(out var store, out _);

        vm.HistoryCaptureEnabled = false;
        vm.HistoryMaxEntries = 500;
        vm.HistoryMaxSizeMiB = 25;
        vm.HistoryResponseCapKiB = 128;

        store.Current.History.Enabled.ShouldBeFalse();
        store.Current.History.MaxEntries.ShouldBe(500);
        store.Current.History.MaxBytes.ShouldBe(25L * 1024 * 1024); // MiB → bytes
        store.Current.History.ResponseCapBytes.ShouldBe(128 * 1024); // KiB → bytes
    }

    [Fact]
    public void Resetting_history_max_entries_restores_the_default()
    {
        var vm = Create(out var store, out _);
        vm.HistoryMaxEntries = 7;

        vm.ResetSettingCommand.Execute("historyMaxEntries");

        vm.HistoryMaxEntries.ShouldBe(StudioSettings.Defaults().History.MaxEntries);
        store.Current.History.MaxEntries.ShouldBe(StudioSettings.Defaults().History.MaxEntries);
    }

    // ── Security audit (SEC-027) ─────────────────────────────────────────────

    private static SettingsDocumentViewModel CreateWithSecrets(FakeSecretStore secrets, FakeDialogService dialogs)
        => new(new FakeSettingsStore(), new FakeThemeService(), dialogs, new FakeProtocService(), secrets: secrets,
            new FakeUpdateService(), new FakeLauncherService());

    [Fact]
    public async Task Security_section_lists_stored_secret_keyrefs()
    {
        var ct = TestContext.Current.CancellationToken;
        var secrets = new FakeSecretStore();
        await secrets.SetAsync("studio/v1/a", "x", ct);
        await secrets.SetAsync("studio/v1/b", "y", ct);
        var vm = CreateWithSecrets(secrets, new FakeDialogService());

        await vm.RefreshSecretsCommand.ExecuteAsync(null);

        vm.SecretKeyRefs.ShouldBe(["studio/v1/a", "studio/v1/b"], ignoreOrder: true);
        vm.HasNoSecretKeyRefs.ShouldBeFalse();
    }

    [Fact]
    public async Task Deleting_a_secret_removes_it_after_confirmation()
    {
        var ct = TestContext.Current.CancellationToken;
        var secrets = new FakeSecretStore();
        await secrets.SetAsync("studio/v1/a", "x", ct);
        var dialogs = new FakeDialogService { ConfirmResult = true };
        var vm = CreateWithSecrets(secrets, dialogs);
        await vm.RefreshSecretsCommand.ExecuteAsync(null);

        await vm.DeleteSecretCommand.ExecuteAsync("studio/v1/a");

        (await secrets.ExistsAsync("studio/v1/a", ct)).ShouldBeFalse();
        vm.SecretKeyRefs.ShouldBeEmpty();
        vm.HasNoSecretKeyRefs.ShouldBeTrue();
    }

    [Fact]
    public async Task Declining_the_delete_confirmation_keeps_the_secret()
    {
        var ct = TestContext.Current.CancellationToken;
        var secrets = new FakeSecretStore();
        await secrets.SetAsync("studio/v1/a", "x", ct);
        var dialogs = new FakeDialogService { ConfirmResult = false };
        var vm = CreateWithSecrets(secrets, dialogs);
        await vm.RefreshSecretsCommand.ExecuteAsync(null);

        await vm.DeleteSecretCommand.ExecuteAsync("studio/v1/a");

        (await secrets.ExistsAsync("studio/v1/a", ct)).ShouldBeTrue(); // declined → untouched
        vm.SecretKeyRefs.ShouldHaveSingleItem();
    }
}
