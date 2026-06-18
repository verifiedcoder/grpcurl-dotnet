using GrpCurl.Net.Studio.Services;
using GrpCurl.Net.Studio.ViewModels.Models;

namespace GrpCurl.Net.Studio.Tests.Unit;

/// <summary>FR-156: the default update service reports a version and the right releases URL per channel.</summary>
public sealed class UpdateServiceTests
{
    [Fact]
    public void Reports_a_non_empty_current_version()
        => new UpdateService().CurrentVersion.ShouldNotBeNullOrWhiteSpace();

    [Fact]
    public void Stable_points_at_the_latest_release_and_preview_at_all_releases()
    {
        var service = new UpdateService();

        service.ReleasesUrl(UpdateChannel.Stable).ShouldEndWith("/releases/latest");
        service.ReleasesUrl(UpdateChannel.Preview).ShouldEndWith("/releases");
        service.ReleasesUrl(UpdateChannel.Stable).ShouldContain("verifiedcoder/grpcurl-dotnet");
    }
}
