using System.Text.Json;
using GrpCurl.Net.Studio.Services;
using GrpCurl.Net.Studio.TestSupport;
using GrpCurl.Net.Studio.ViewModels.Documents;
using GrpCurl.Net.Studio.ViewModels.Models;
using GrpCurl.Net.Studio.ViewModels.Services;

namespace GrpCurl.Net.Studio.Tests.Unit;

public sealed class UpdateUrlPolicyTests
{
    [Theory]
    [InlineData("https://github.com/verifiedcoder/grpcurl-dotnet/releases/latest", true)]
    [InlineData("HTTPS://GITHUB.COM/releases", true)]
    [InlineData("https://api.github.com/releases", true)]
    [InlineData("https://a.b.github.com/releases", true)]
    [InlineData("https://github.com:8443/any/path?q=1#fragment", true)]
    [InlineData("https://name@github.com/releases", true)]
    [InlineData("https://github.com.evil.example/releases", false)]
    [InlineData("https://evilgithub.com/releases", false)]
    [InlineData("https://github.com@evil.example/releases", false)]
    [InlineData("https://github.com./releases", false)]
    [InlineData("https://github.com\\@evil.example/releases", false)]
    [InlineData("http://github.com/releases", false)]
    [InlineData("file:///tmp/release", false)]
    [InlineData("ms-msdt:/id PCWDiagnostic", false)]
    [InlineData("//github.com/releases", false)]
    [InlineData("/releases", false)]
    [InlineData("https://", false)]
    [InlineData("not a URL", false)]
    [InlineData("", false)]
    [InlineData("   ", false)]
    [InlineData(null, false)]
    public async Task Policy_is_applied_at_the_feed_boundary(string? url, bool allowed)
    {
        UpdateUrlPolicy.IsAllowed(url).ShouldBe(allowed);
        var json = JsonSerializer.Serialize(new[] { new { tag_name = "v999.0.0", html_url = url } });
        var service = new UpdateService(_ => Task.FromResult(json));
        var result = await service.CheckForUpdateAsync(UpdateChannel.Stable, TestContext.Current.CancellationToken);
        result.Availability.ShouldBe(UpdateAvailability.UpdateAvailable);
        result.ReleaseUrl.ShouldBe(allowed ? url : string.Empty);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"html_url\":null}")]
    [InlineData("{\"html_url\":42}")]
    [InlineData("{\"html_url\":true}")]
    [InlineData("{\"html_url\":[]}")]
    [InlineData("{\"html_url\":{}}")]
    public async Task Unavailable_feed_url_preserves_update_and_opens_channel_fallback(string fields)
    {
        var json = "[{\"tag_name\":\"v999.0.0\"," + fields[1..];
        json = json.Replace(",}", "}", StringComparison.Ordinal) + "]";
        var service = new UpdateService(_ => Task.FromResult(json));
        foreach (var channel in Enum.GetValues<UpdateChannel>())
        {
            var launcher = new FakeLauncherService();
            using var vm = Create(service, launcher);
            vm.UpdateChannel = channel;
            await vm.CheckForUpdatesCommand.ExecuteAsync(null);
            vm.UpdateAvailable.ShouldBeTrue();
            vm.LatestVersion.ShouldBe("v999.0.0");
            launcher.LaunchCount.ShouldBe(0);
            await vm.OpenLatestReleaseCommand.ExecuteAsync(null);
            launcher.LastUri.ShouldBe(service.ReleasesUrl(channel));
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" \t")]
    public async Task Missing_direct_result_uses_validated_fallback(string? url)
    {
        var service = new FakeUpdateService { CheckResult = new UpdateCheckResult(UpdateAvailability.UpdateAvailable, "v999.0.0", url) };
        var launcher = new FakeLauncherService();
        using var vm = Create(service, launcher);
        await vm.CheckForUpdatesCommand.ExecuteAsync(null);
        await vm.OpenLatestReleaseCommand.ExecuteAsync(null);
        launcher.LastUri.ShouldBe(service.ReleasesUrl(vm.UpdateChannel));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Unsafe_release_or_fallback_only_offers_copy(bool fallback, bool confirm)
    {
        const string unsafeUrl = "https://github.com.evil.example/update";
        var service = new FakeUpdateService
        {
            CheckResult = fallback ? UpdateCheckResult.Failed : UpdateCheckResult.Available("v999.0.0", unsafeUrl),
            ReleasesUrlOverride = unsafeUrl
        };
        var dialogs = new FakeDialogService { ConfirmResult = confirm };
        var clipboard = new FakeClipboardService();
        var launcher = new FakeLauncherService();
        using var vm = Create(service, launcher, dialogs, clipboard);
        await vm.CheckForUpdatesCommand.ExecuteAsync(null);
        if (!fallback) { await vm.OpenLatestReleaseCommand.ExecuteAsync(null); }
        launcher.LaunchCount.ShouldBe(0);
        dialogs.LastConfirmTitle.ShouldBe("Unsupported link");
        dialogs.LastConfirmBody.ShouldNotBeNull().ShouldContain("HTTPS");
        dialogs.LastConfirmBody.ShouldContain("github.com or its subdomains");
        clipboard.Text.ShouldBe(confirm ? unsafeUrl : null);
    }

    [Fact]
    public async Task Missing_release_with_unsafe_fallback_is_also_blocked_without_clipboard()
    {
        var service = new FakeUpdateService
        {
            CheckResult = UpdateCheckResult.Available("v999.0.0", ""),
            ReleasesUrlOverride = "file:///tmp/update"
        };
        var launcher = new FakeLauncherService();
        using var vm = Create(service, launcher);
        await vm.CheckForUpdatesCommand.ExecuteAsync(null);
        await vm.OpenLatestReleaseCommand.ExecuteAsync(null);
        launcher.LaunchCount.ShouldBe(0);
        vm.UpdateStatus.ShouldNotBeNull().ShouldContain("HTTPS");
    }

    [Fact]
    public async Task Failed_check_clears_previous_release_state_and_reports_browser_failure()
    {
        var service = new FakeUpdateService { CheckResult = UpdateCheckResult.Available("v999.0.0", "https://github.com/release") };
        var launcher = new FakeLauncherService { Result = false };
        using var vm = Create(service, launcher);
        await vm.CheckForUpdatesCommand.ExecuteAsync(null);
        service.CheckResult = UpdateCheckResult.Failed;
        await vm.CheckForUpdatesCommand.ExecuteAsync(null);
        vm.UpdateAvailable.ShouldBeFalse();
        vm.LatestVersion.ShouldBeNull();
        vm.OpenLatestReleaseCommand.CanExecute(null).ShouldBeFalse();
        vm.UpdateStatus.ShouldBe("Couldn't open the releases page in your browser.");
        launcher.LastUri.ShouldBe(service.ReleasesUrl(vm.UpdateChannel));
    }

    private static SettingsDocumentViewModel Create(IUpdateService service, FakeLauncherService launcher,
        FakeDialogService? dialogs = null, FakeClipboardService? clipboard = null)
        => new(new InMemorySettingsStore(), new FakeThemeService(), dialogs ?? new FakeDialogService(),
            updates: service, launcher: launcher, clipboard: clipboard);
}
