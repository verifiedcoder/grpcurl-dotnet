using GrpCurl.Net.Studio.ViewModels.Models;

namespace GrpCurl.Net.Studio.ViewModels.Services;

/// <summary>
///     Supplies update information for the Settings → Updates panel (FR-156): the running version and the
///     releases page to send the user to for a manual, consent-respecting update (ADR-011 — updates are
///     never applied without consent). Abstracted so the Settings view model is unit-testable without
///     reading the running assembly or hard-coding URLs.
/// </summary>
public interface IUpdateService
{
    /// <summary>The running application version, e.g. <c>1.0.0</c>.</summary>
    string CurrentVersion { get; }

    /// <summary>The releases page to open for <paramref name="channel" /> (manual "Check now", FR-156).</summary>
    string ReleasesUrl(UpdateChannel channel);

    /// <summary>
    ///     FR-156: compares the running version against the latest release on <paramref name="channel" /> (GitHub
    ///     releases API). Offline-safe — any network/parse failure returns <see cref="UpdateCheckResult.Failed" />
    ///     rather than throwing. No download or apply is performed (ADR-011).
    /// </summary>
    Task<UpdateCheckResult> CheckForUpdateAsync(UpdateChannel channel, CancellationToken cancellationToken = default);
}
