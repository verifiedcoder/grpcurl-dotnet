using System.Text.Json;
using GrpCurl.Net.Studio.Services;
using GrpCurl.Net.Studio.TestSupport;
using GrpCurl.Net.Studio.ViewModels.Documents;
using GrpCurl.Net.Studio.ViewModels.Models;

namespace GrpCurl.Net.Studio.Tests.Unit;

public sealed class UpdateUrlBoundaryTests
{
    [Theory]
    [InlineData("file:///tmp/update")]
    [InlineData("ms-msdt:/id PCWDiagnostic")]
    [InlineData("https://evil.example/update")]
    public async Task Feed_discards_unsafe_links_without_losing_version_information(string url)
    {
        var json = JsonSerializer.Serialize(new[] { new { tag_name = "v999.0.0", html_url = url } });
        var service = new UpdateService(_ => Task.FromResult(json));
        var result = await service.CheckForUpdateAsync(UpdateChannel.Stable, TestContext.Current.CancellationToken);
        result.Availability.ShouldBe(UpdateAvailability.UpdateAvailable);
        result.LatestVersion.ShouldBe("v999.0.0");
        result.ReleaseUrl.ShouldBe(string.Empty);
    }

    [Theory]
    [InlineData("file:///tmp/update")]
    [InlineData("ms-msdt:/id PCWDiagnostic")]
    [InlineData("https://evil.example/update")]
    public async Task Direct_service_result_cannot_launch_an_unsafe_link(string url)
    {
        var updates = new FakeUpdateService { CheckResult = UpdateCheckResult.Available("v999.0.0", url) };
        var launcher = new FakeLauncherService();
        using var vm = new SettingsDocumentViewModel(new InMemorySettingsStore(), new FakeThemeService(),
            new FakeDialogService(), updates: updates, launcher: launcher);
        await vm.CheckForUpdatesCommand.ExecuteAsync(null);
        await vm.OpenLatestReleaseCommand.ExecuteAsync(null);
        launcher.LaunchCount.ShouldBe(0);
    }
}
