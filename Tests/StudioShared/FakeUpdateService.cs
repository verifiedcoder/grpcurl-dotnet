using GrpCurl.Net.Studio.ViewModels.Models;
using GrpCurl.Net.Studio.ViewModels.Services;

namespace GrpCurl.Net.Studio.TestSupport;

/// <summary>Scripted <see cref="IUpdateService" /> for Settings → Updates tests (FR-156).</summary>
public sealed class FakeUpdateService : IUpdateService
{
    public string CurrentVersion { get; set; } = "9.9.9";

    public string ReleasesUrl(UpdateChannel channel)
        => channel == UpdateChannel.Stable ? "https://example.test/releases/latest" : "https://example.test/releases";
}
