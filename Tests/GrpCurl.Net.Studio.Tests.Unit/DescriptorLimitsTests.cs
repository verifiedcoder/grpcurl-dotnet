using GrpCurl.Net.DescriptorSources;
using GrpCurl.Net.Studio.Services;
using GrpCurl.Net.Studio.TestSupport;
using GrpCurl.Net.Studio.Tests.Unit.Fakes;
using GrpCurl.Net.Studio.ViewModels.Documents;
using GrpCurl.Net.Studio.ViewModels.Models.Connections;

namespace GrpCurl.Net.Studio.Tests.Unit;

/// <summary>
///     FR-157 Descriptor limits: the app-wide cap defaults (Settings) and the resolution order applied by
///     <see cref="DescriptorService" /> — per-connection override (FR-049) wins, then the app default, then
///     Core's default.
/// </summary>
public sealed class DescriptorLimitsTests
{
    // ── Settings VM (FR-157) ─────────────────────────────────────────────────

    [Fact]
    public void Descriptor_limits_load_from_current_in_friendly_units()
    {
        var store = new FakeSettingsStore();
        store.Current.DescriptorLimits.MaxProtosetFileBytes = 128L * 1024 * 1024;
        store.Current.DescriptorLimits.MaxSymbols = 9999;

        var vm = new SettingsDocumentViewModel(store, new FakeThemeService(), new FakeDialogService(), new FakeProtocService());

        vm.DescriptorMaxProtosetMiB.ShouldBe(128);
        vm.DescriptorMaxProtosetMiBChanged.ShouldBeTrue();   // differs from the 64 MiB Core default
        vm.DescriptorMaxSymbols.ShouldBe(9999);
        vm.DescriptorMaxReflectionMiBChanged.ShouldBeFalse(); // unchanged
    }

    [Fact]
    public void Changing_a_descriptor_limit_persists_in_bytes()
    {
        var store = new FakeSettingsStore();
        var vm = new SettingsDocumentViewModel(store, new FakeThemeService(), new FakeDialogService(), new FakeProtocService());

        vm.DescriptorMaxProtosetMiB = 32;
        vm.DescriptorMaxFileDescriptors = 4096;

        store.Current.DescriptorLimits.MaxProtosetFileBytes.ShouldBe(32L * 1024 * 1024); // MiB → bytes
        store.Current.DescriptorLimits.MaxFileDescriptors.ShouldBe(4096);
    }

    [Fact]
    public void Resetting_a_descriptor_limit_restores_the_core_default()
    {
        var store = new FakeSettingsStore();
        var vm = new SettingsDocumentViewModel(store, new FakeThemeService(), new FakeDialogService(), new FakeProtocService());
        vm.DescriptorMaxSymbols = 7;
        vm.DescriptorMaxSymbolsChanged.ShouldBeTrue();

        vm.ResetSettingCommand.Execute("descriptorSymbols");

        vm.DescriptorMaxSymbols.ShouldBe(DescriptorSourceOptions.DefaultMaxSymbols);
        vm.DescriptorMaxSymbolsChanged.ShouldBeFalse();
    }

    // ── DescriptorService resolution (FR-049 / FR-157) ───────────────────────

    private static SavedConnection Conn(Action<DescriptorSourceConfig>? configure = null)
    {
        var connection = new SavedConnection { Name = "c", Address = "h:1" };
        configure?.Invoke(connection.DescriptorSource);
        return connection;
    }

    [Fact]
    public void With_no_override_and_no_settings_core_defaults_are_used()
    {
        var service = new DescriptorService();

        service.BuildDescriptorOptions(Conn()).ShouldBeNull(); // null → Core applies its own defaults
    }

    [Fact]
    public void An_app_wide_default_applies_when_the_connection_has_no_override()
    {
        var store = new FakeSettingsStore();
        store.Current.DescriptorLimits.MaxSymbols = 123;
        var service = new DescriptorService(settings: store);

        var options = service.BuildDescriptorOptions(Conn()).ShouldNotBeNull();

        options.MaxSymbols.ShouldBe(123);                                                 // app-wide default
        options.MaxFileDescriptors.ShouldBe(DescriptorSourceOptions.DefaultMaxFileDescriptors); // untouched → Core
    }

    [Fact]
    public void A_per_connection_override_wins_over_the_app_wide_default()
    {
        var store = new FakeSettingsStore();
        store.Current.DescriptorLimits.MaxSymbols = 123; // app default
        var service = new DescriptorService(settings: store);

        var options = service.BuildDescriptorOptions(Conn(d => d.MaxSymbols = 50)).ShouldNotBeNull();

        options.MaxSymbols.ShouldBe(50); // FR-049 override beats the app default
    }
}
