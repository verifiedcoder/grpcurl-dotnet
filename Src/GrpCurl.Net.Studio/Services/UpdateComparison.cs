using GrpCurl.Net.Studio.ViewModels.Models;

namespace GrpCurl.Net.Studio.Services;

/// <summary>One GitHub release, reduced to what the update check needs.</summary>
internal sealed record ReleaseInfo(string Tag, bool Prerelease, string HtmlUrl);

/// <summary>
///     FR-156: pure version-comparison for the update check. Picks the newest release on the channel
///     (Stable excludes prereleases; Preview includes them), parses its numeric version, and compares it to
///     the running version. Kept free of I/O so the rules are deterministically testable.
/// </summary>
internal static class UpdateComparison
{
    public static UpdateCheckResult Evaluate(string currentVersion, IReadOnlyList<ReleaseInfo> releases, UpdateChannel channel)
    {
        if (ParseVersion(currentVersion) is not { } current)
        {
            return UpdateCheckResult.Failed;
        }

        var newest = releases
            .Where(r => channel == UpdateChannel.Preview || !r.Prerelease)
            .Select(r => (Release: r, Version: ParseVersion(r.Tag)))
            .Where(x => x.Version is not null)
            .OrderByDescending(x => x.Version)
            .FirstOrDefault();

        if (newest.Release is null)
        {
            return UpdateCheckResult.Failed; // nothing comparable on this channel
        }

        return newest.Version > current
            ? UpdateCheckResult.Available(newest.Release.Tag, newest.Release.HtmlUrl)
            : UpdateCheckResult.UpToDate;
    }

    // Tolerant: strips a leading "v", any "-prerelease" or "+build" suffix, then parses the numeric core.
    internal static Version? ParseVersion(string? tag)
    {
        if (string.IsNullOrWhiteSpace(tag))
        {
            return null;
        }

        var core = tag.Trim();

        if (core.StartsWith('v') || core.StartsWith('V'))
        {
            core = core[1..];
        }

        var cut = core.AsSpan().IndexOfAny('-', '+');

        if (cut >= 0)
        {
            core = core[..cut];
        }

        return Version.TryParse(core, out var version) ? version : null;
    }
}
