using GrpCurl.Net.Studio.Tests.Unit.Fakes;
using GrpCurl.Net.Studio.TestSupport;
using GrpCurl.Net.Studio.ViewModels.Documents;
using GrpCurl.Net.Studio.ViewModels.Models;

namespace GrpCurl.Net.Studio.Tests.Unit;

public sealed class SettingsDocumentViewModelTests
{
    private static SettingsDocumentViewModel Create(out FakeSettingsStore store, out FakeThemeService theme)
    {
        store = new FakeSettingsStore();
        theme = new FakeThemeService();
        return new SettingsDocumentViewModel(store, theme);
    }

    [Fact]
    public void Seeds_from_current_settings_without_persisting()
    {
        var store = new FakeSettingsStore();
        store.Current.General.CliShellDialect = ShellDialect.PowerShell;
        store.Current.Editor.FontSize = 16;

        var vm = new SettingsDocumentViewModel(store, new FakeThemeService());

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
}
