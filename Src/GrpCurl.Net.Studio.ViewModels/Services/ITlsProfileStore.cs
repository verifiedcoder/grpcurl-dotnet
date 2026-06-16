using GrpCurl.Net.Studio.ViewModels.Models.Connections;

namespace GrpCurl.Net.Studio.ViewModels.Services;

/// <summary>
///     Manages the workspace's named TLS profiles (FR-030). Profiles are workspace-level, so this sits
///     over <see cref="IWorkspaceStore" /> and persists without disturbing the connection list. The
///     connection editor's picker (E2.2 PR-C) and the profile manager (PR-D) both go through here.
/// </summary>
public interface ITlsProfileStore
{
    /// <summary>The profiles in the live workspace, newest edits reflected.</summary>
    IReadOnlyList<TlsProfile> Profiles { get; }

    /// <summary>Inserts a new profile or replaces the existing one with the same <see cref="TlsProfile.Id" />.</summary>
    Task SaveAsync(TlsProfile profile, CancellationToken cancellationToken = default);

    /// <summary>How many saved connections currently reference the profile (FR-038).</summary>
    int UsageCount(string profileId);
}
