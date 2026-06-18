using System.Reflection;
using System.Text.Json;
using GrpCurl.Net.Studio.ViewModels.Models;
using GrpCurl.Net.Studio.ViewModels.Services;

namespace GrpCurl.Net.Studio.Services;

/// <summary>
///     Default <see cref="IUpdateService" /> (FR-156). The version comes from the entry assembly's
///     informational version (falling back to a constant); the releases URL points at this repo's GitHub
///     releases — the Stable channel at the latest release, the Preview channel at the full list.
///     <see cref="CheckForUpdateAsync" /> compares the running version against the GitHub releases API
///     (offline-safe); in-app applying is still deferred (ADR-011 / SPEC-080 packaging) — the user updates
///     manually via the releases page.
/// </summary>
internal sealed class UpdateService : IUpdateService
{
    private const string RepoUrl = "https://github.com/verifiedcoder/grpcurl-dotnet";
    private const string ApiReleasesUrl = "https://api.github.com/repos/verifiedcoder/grpcurl-dotnet/releases";
    private const string FallbackVersion = "1.0.0";

    private static readonly HttpClient Http = CreateClient();

    private readonly Func<CancellationToken, Task<string>> _fetchReleasesJson;

    public UpdateService()
        : this(DefaultFetchAsync)
    {
    }

    // Test seam: supply the releases JSON directly so the parse + comparison run without a network call.
    internal UpdateService(Func<CancellationToken, Task<string>> fetchReleasesJson)
        => _fetchReleasesJson = fetchReleasesJson;

    public string CurrentVersion { get; } = ReadVersion();

    public string ReleasesUrl(UpdateChannel channel)
        => channel == UpdateChannel.Stable ? $"{RepoUrl}/releases/latest" : $"{RepoUrl}/releases";

    public async Task<UpdateCheckResult> CheckForUpdateAsync(UpdateChannel channel, CancellationToken cancellationToken = default)
    {
        try
        {
            var releases = ParseReleases(await _fetchReleasesJson(cancellationToken).ConfigureAwait(false));
            return UpdateComparison.Evaluate(CurrentVersion, releases, channel);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or InvalidOperationException)
        {
            return UpdateCheckResult.Failed; // offline / unreachable / unparseable — never throw to the UI
        }
    }

    private static IReadOnlyList<ReleaseInfo> ParseReleases(string json)
    {
        using var document = JsonDocument.Parse(json);
        var releases = new List<ReleaseInfo>();

        if (document.RootElement.ValueKind != JsonValueKind.Array)
        {
            return releases;
        }

        foreach (var element in document.RootElement.EnumerateArray())
        {
            if (!element.TryGetProperty("tag_name", out var tag) || tag.GetString() is not { Length: > 0 } tagName)
            {
                continue;
            }

            var prerelease = element.TryGetProperty("prerelease", out var pre) && pre.ValueKind == JsonValueKind.True;
            var htmlUrl = element.TryGetProperty("html_url", out var url) ? url.GetString() ?? string.Empty : string.Empty;
            releases.Add(new ReleaseInfo(tagName, prerelease, htmlUrl));
        }

        return releases;
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("GrpCurl.Net.Studio"); // GitHub requires a User-Agent
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return client;
    }

    private static async Task<string> DefaultFetchAsync(CancellationToken cancellationToken)
    {
        using var response = await Http.GetAsync(ApiReleasesUrl, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
    }

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
