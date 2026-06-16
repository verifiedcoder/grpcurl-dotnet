using GrpCurl.Net.Studio.ViewModels.Models.Connections;
using GrpCurl.Net.Studio.ViewModels.Services;

namespace GrpCurl.Net.Studio.Services;

/// <summary>
///     Default <see cref="ITlsProfileStore" />. Reads from and writes to the live
///     <see cref="IWorkspaceStore.Current" />, rebuilding the workspace from its current state so a
///     profile save never drops the connection list (the inverse hazard to the connections pane, which
///     must likewise preserve profiles).
/// </summary>
internal sealed class TlsProfileStore(IWorkspaceStore workspace) : ITlsProfileStore
{
    public IReadOnlyList<TlsProfile> Profiles => workspace.Current.TlsProfiles;

    public Task SaveAsync(TlsProfile profile, CancellationToken cancellationToken = default)
    {
        var current = workspace.Current;
        var profiles = current.TlsProfiles.Where(p => p.Id != profile.Id).Append(profile).ToList();

        var updated = new WorkspaceModel
        {
            SchemaVersion = current.SchemaVersion,
            Connections = [.. current.Connections],
            TlsProfiles = profiles
        };

        return workspace.SaveAsync(updated, cancellationToken);
    }

    public int UsageCount(string profileId)
        => workspace.Current.Connections.Count(c => c.TlsProfileId == profileId);
}
