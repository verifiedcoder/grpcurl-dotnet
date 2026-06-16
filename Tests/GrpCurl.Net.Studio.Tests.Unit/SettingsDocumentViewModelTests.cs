using GrpCurl.Net.Studio.Tests.Unit.Fakes;
using GrpCurl.Net.Studio.TestSupport;
using GrpCurl.Net.Studio.ViewModels.Documents;
using GrpCurl.Net.Studio.ViewModels.Models;
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
}
