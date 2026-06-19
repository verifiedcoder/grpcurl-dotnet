using GrpCurl.Net.Studio.Services;
using GrpCurl.Net.Studio.ViewModels.Models;

namespace GrpCurl.Net.Studio.Tests.UI;

/// <summary>
///     Filesystem round-trip for the JSON settings store. Not a UI test (no headless session),
///     but it lives here because the store is internal to the app assembly, which this project
///     references.
/// </summary>
public sealed class JsonSettingsStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "grpcn-studio-tests-" + Guid.NewGuid().ToString("N"));

    private string SettingsPath => Path.Combine(_dir, "settings.json");

    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, recursive: true);
        }
    }

    [Fact]
    public async Task Save_then_load_round_trips_theme()
    {
        var ct = TestContext.Current.CancellationToken;
        var store = new JsonSettingsStore(SettingsPath);
        var settings = StudioSettings.Defaults();
        settings.Appearance.Theme = "dark";

        await store.SaveAsync(settings, ct);

        File.Exists(SettingsPath).ShouldBeTrue();

        var reloaded = await new JsonSettingsStore(SettingsPath).LoadAsync(ct);

        reloaded.Appearance.Theme.ShouldBe("dark");
        reloaded.SchemaVersion.ShouldBe(1);
    }

    [Fact]
    public async Task Load_missing_file_returns_defaults()
    {
        var store = new JsonSettingsStore(SettingsPath);

        var settings = await store.LoadAsync(TestContext.Current.CancellationToken);

        settings.Appearance.Theme.ShouldBe("system");
    }

    [Fact]
    public async Task Load_unknown_keys_round_trip_through_overflow()
    {
        var ct = TestContext.Current.CancellationToken;
        _ = Directory.CreateDirectory(_dir);
        await File.WriteAllTextAsync(SettingsPath,
            """{"schemaVersion":1,"appearance":{"theme":"light","uiScale":1.0},"futureKey":{"a":1}}""", ct);

        var store = new JsonSettingsStore(SettingsPath);
        var settings = await store.LoadAsync(ct);

        _ = settings.Overflow.ShouldNotBeNull();
        settings.Overflow!.ShouldContainKey("futureKey");

        // Re-save and confirm the unknown key survives.
        await store.SaveAsync(settings, ct);
        var text = await File.ReadAllTextAsync(SettingsPath, ct);
        text.ShouldContain("futureKey");
    }
}
