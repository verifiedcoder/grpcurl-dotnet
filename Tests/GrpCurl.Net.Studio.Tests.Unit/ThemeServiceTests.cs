using GrpCurl.Net.Studio.Tests.Unit.Fakes;
using GrpCurl.Net.Studio.ViewModels.Models;
using GrpCurl.Net.Studio.ViewModels.Services;

namespace GrpCurl.Net.Studio.Tests.Unit;

public sealed class ThemeServiceTests
{
    [Fact]
    public void Current_seeds_from_the_persisted_setting()
    {
        var store = new FakeSettingsStore();
        store.Current.Appearance.Theme = "dark";

        new ThemeService(store).Current.ShouldBe(AppTheme.Dark);
    }

    [Fact]
    public async Task SetAsync_updates_current_persists_and_notifies()
    {
        var store = new FakeSettingsStore();
        var service = new ThemeService(store);
        var notified = false;
        service.PropertyChanged += (_, e) => notified |= e.PropertyName == nameof(IThemeService.Current);

        await service.SetAsync(AppTheme.Light, TestContext.Current.CancellationToken);

        service.Current.ShouldBe(AppTheme.Light);
        store.Current.Appearance.Theme.ShouldBe("light");
        store.SaveCount.ShouldBe(1);
        notified.ShouldBeTrue();
    }
}
