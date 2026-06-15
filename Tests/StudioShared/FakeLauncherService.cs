using GrpCurl.Net.Studio.ViewModels.Services;

namespace GrpCurl.Net.Studio.TestSupport;

public sealed class FakeLauncherService : ILauncherService
{
    public string? LastUri { get; private set; }

    public int LaunchCount { get; private set; }

    public bool Result { get; set; } = true;

    public Task<bool> LaunchUriAsync(string uri, CancellationToken cancellationToken = default)
    {
        LastUri = uri;
        LaunchCount++;
        return Task.FromResult(Result);
    }
}
