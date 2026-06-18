using GrpCurl.Net.Studio.ViewModels.Models;
using GrpCurl.Net.Studio.ViewModels.Services;

namespace GrpCurl.Net.Studio.TestSupport;

/// <summary>Scripted <see cref="IUpdateService" /> for Settings → Updates tests (FR-156).</summary>
public sealed class FakeUpdateService : IUpdateService
{
    public string CurrentVersion { get; set; } = "9.9.9";

    public string ReleasesUrl(UpdateChannel channel)
        => channel == UpdateChannel.Stable ? "https://example.test/releases/latest" : "https://example.test/releases";

    /// <summary>Scripted result for <see cref="CheckForUpdateAsync" /> (FR-156).</summary>
    public UpdateCheckResult CheckResult { get; set; } = UpdateCheckResult.UpToDate;

    public UpdateChannel? LastCheckedChannel { get; private set; }

    public Task<UpdateCheckResult> CheckForUpdateAsync(UpdateChannel channel, CancellationToken cancellationToken = default)
    {
        LastCheckedChannel = channel;
        return Task.FromResult(CheckResult);
    }
}
