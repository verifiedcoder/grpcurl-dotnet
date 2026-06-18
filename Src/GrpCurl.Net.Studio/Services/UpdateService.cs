using System.Reflection;
using GrpCurl.Net.Studio.ViewModels.Models;
using GrpCurl.Net.Studio.ViewModels.Services;

namespace GrpCurl.Net.Studio.Services;

/// <summary>
///     Default <see cref="IUpdateService" /> (FR-156). The version comes from the entry assembly's
///     informational version (falling back to a constant); the releases URL points at this repo's GitHub
///     releases — the Stable channel at the latest release, the Preview channel at the full list. Automatic
///     checking and in-app applying are deferred (ADR-011 / SPEC-080 packaging); "Check now" opens the page.
/// </summary>
internal sealed class UpdateService : IUpdateService
{
    private const string RepoUrl = "https://github.com/verifiedcoder/grpcurl-dotnet";
    private const string FallbackVersion = "1.0.0";

    public string CurrentVersion { get; } = ReadVersion();

    public string ReleasesUrl(UpdateChannel channel)
        => channel == UpdateChannel.Stable ? $"{RepoUrl}/releases/latest" : $"{RepoUrl}/releases";

    private static string ReadVersion()
    {
        var informational = (Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly())
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;

        if (string.IsNullOrWhiteSpace(informational))
        {
            return FallbackVersion;
        }

        // Strip any "+<build metadata>" suffix the SDK appends to the informational version.
        var plus = informational.IndexOf('+');
        return plus >= 0 ? informational[..plus] : informational;
    }
}
