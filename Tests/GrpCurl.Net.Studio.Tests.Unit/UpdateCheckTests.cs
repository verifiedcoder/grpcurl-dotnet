using GrpCurl.Net.Studio.Services;
using GrpCurl.Net.Studio.ViewModels.Models;

namespace GrpCurl.Net.Studio.Tests.Unit;

/// <summary>FR-156: the version-comparison update check — pure comparison + the service's parse/offline path.</summary>
public sealed class UpdateCheckTests
{
    private static ReleaseInfo Rel(string tag, bool prerelease = false) => new(tag, prerelease, $"https://example.test/{tag}");

    [Fact]
    public void A_newer_stable_release_is_an_available_update()
    {
        var result = UpdateComparison.Evaluate("1.0.0", [Rel("v1.2.0")], UpdateChannel.Stable);

        result.Availability.ShouldBe(UpdateAvailability.UpdateAvailable);
        result.LatestVersion.ShouldBe("v1.2.0");
        result.ReleaseUrl.ShouldBe("https://example.test/v1.2.0");
    }

    [Fact]
    public void The_same_version_is_up_to_date()
        => UpdateComparison.Evaluate("1.2.0", [Rel("v1.2.0")], UpdateChannel.Stable)
            .Availability.ShouldBe(UpdateAvailability.UpToDate);

    [Fact]
    public void The_stable_channel_ignores_prereleases()
    {
        // A newer prerelease exists, but the newest *stable* equals the current version.
        var result = UpdateComparison.Evaluate("1.0.0", [Rel("v1.2.0", prerelease: true), Rel("v1.0.0")], UpdateChannel.Stable);

        result.Availability.ShouldBe(UpdateAvailability.UpToDate);
    }

    [Fact]
    public void The_preview_channel_includes_prereleases()
        => UpdateComparison.Evaluate("1.0.0", [Rel("v1.2.0-preview.1", prerelease: true)], UpdateChannel.Preview)
            .Availability.ShouldBe(UpdateAvailability.UpdateAvailable);

    [Fact]
    public void The_newest_of_several_releases_wins()
        => UpdateComparison.Evaluate("1.0.0", [Rel("v1.1.0"), Rel("v1.3.0"), Rel("v1.2.0")], UpdateChannel.Stable)
            .LatestVersion.ShouldBe("v1.3.0");

    [Fact]
    public void An_unparseable_current_version_fails_the_check()
        => UpdateComparison.Evaluate("nightly", [Rel("v1.2.0")], UpdateChannel.Stable)
            .Availability.ShouldBe(UpdateAvailability.CheckFailed);

    [Fact]
    public void No_comparable_release_fails_the_check()
        => UpdateComparison.Evaluate("1.0.0", [], UpdateChannel.Stable).Availability.ShouldBe(UpdateAvailability.CheckFailed);

    // ── UpdateService parse + offline-safety ─────────────────────────────────

    [Fact]
    public async Task The_service_reports_an_available_update_from_the_releases_json()
    {
        const string json = "[{\"tag_name\":\"v999.0.0\",\"prerelease\":false,\"html_url\":\"https://example.test/v999\"}]";
        var service = new UpdateService(_ => Task.FromResult(json));

        var result = await service.CheckForUpdateAsync(UpdateChannel.Stable, TestContext.Current.CancellationToken);

        result.Availability.ShouldBe(UpdateAvailability.UpdateAvailable); // current is surely < 999.0.0
        result.LatestVersion.ShouldBe("v999.0.0");
    }

    [Fact]
    public async Task The_service_is_offline_safe()
    {
        var service = new UpdateService(_ => throw new HttpRequestException("offline"));

        var result = await service.CheckForUpdateAsync(UpdateChannel.Stable, TestContext.Current.CancellationToken);

        result.Availability.ShouldBe(UpdateAvailability.CheckFailed);
    }

    [Fact]
    public async Task The_service_treats_a_malformed_response_as_a_failed_check()
    {
        var service = new UpdateService(_ => Task.FromResult("{ not json"));

        (await service.CheckForUpdateAsync(UpdateChannel.Stable, TestContext.Current.CancellationToken))
            .Availability.ShouldBe(UpdateAvailability.CheckFailed);
    }
}
