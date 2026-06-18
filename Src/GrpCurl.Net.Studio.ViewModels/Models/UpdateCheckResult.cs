namespace GrpCurl.Net.Studio.ViewModels.Models;

/// <summary>The outcome of a version-comparison update check (FR-156).</summary>
public enum UpdateAvailability
{
    /// <summary>The running version is the newest on the selected channel.</summary>
    UpToDate,

    /// <summary>A newer release exists on the selected channel.</summary>
    UpdateAvailable,

    /// <summary>The check couldn't complete (offline, GitHub unreachable, or an unparseable response).</summary>
    CheckFailed
}

/// <summary>
///     The result of <see cref="Services.IUpdateService.CheckForUpdateAsync" /> (FR-156): whether a newer release
///     exists and, when it does, its version and release-page URL. No download or apply is implied — updates are
///     always applied by the user with consent (ADR-011).
/// </summary>
public sealed record UpdateCheckResult(UpdateAvailability Availability, string? LatestVersion = null, string? ReleaseUrl = null)
{
    public static UpdateCheckResult UpToDate { get; } = new(UpdateAvailability.UpToDate);

    public static UpdateCheckResult Failed { get; } = new(UpdateAvailability.CheckFailed);

    public static UpdateCheckResult Available(string latestVersion, string releaseUrl)
        => new(UpdateAvailability.UpdateAvailable, latestVersion, releaseUrl);
}
