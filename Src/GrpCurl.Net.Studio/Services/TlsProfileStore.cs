using GrpCurl.Net.Studio.ViewModels.Models.Connections;
using GrpCurl.Net.Studio.ViewModels.Services;

namespace GrpCurl.Net.Studio.Services;

/// <summary>
///     Default <see cref="ITlsProfileStore" />. Reads from and writes to the live
///     <see cref="IWorkspaceStore.Current" />, rebuilding the workspace from its current state so a
///     profile save never drops the connection list (the inverse hazard to the connections pane, which
///     must likewise preserve profiles). Deletes also revert referencing connections and purge the
///     profile's stored PKCS12 password.
/// </summary>
internal sealed class TlsProfileStore(IWorkspaceStore workspace, ISecretStore secrets) : ITlsProfileStore
{
    public IReadOnlyList<TlsProfile> Profiles => workspace.Current.TlsProfiles;

    public Task SaveAsync(TlsProfile profile, CancellationToken cancellationToken = default)
    {
        var current = workspace.Current;
        var profiles = current.TlsProfiles.Where(p => p.Id != profile.Id).Append(profile).ToList();

        return workspace.SaveAsync(
            new WorkspaceModel
            {
                SchemaVersion = current.SchemaVersion,
                Connections = [.. current.Connections],
                TlsProfiles = profiles
            },
            cancellationToken);
    }

    public async Task DeleteAsync(string profileId, CancellationToken cancellationToken = default)
    {
        var current = workspace.Current;
        var profile = current.TlsProfiles.FirstOrDefault(p => p.Id == profileId);

        if (profile is null)
        {
            return;
        }

        // Referencing connections fall back to system-default validation.
        foreach (var connection in current.Connections.Where(c => c.TlsProfileId == profileId))
        {
            connection.TlsProfileId = null;
        }

        await workspace.SaveAsync(
            new WorkspaceModel
            {
                SchemaVersion = current.SchemaVersion,
                Connections = [.. current.Connections],
                TlsProfiles = current.TlsProfiles.Where(p => p.Id != profileId).ToList()
            },
            cancellationToken).ConfigureAwait(false);

        // The PKCS12 password is the profile's only secret; remove it once the profile is gone (SEC-017).
        if (!string.IsNullOrWhiteSpace(profile.ClientCertPasswordSecretRef))
        {
            await secrets.DeleteAsync(profile.ClientCertPasswordSecretRef, cancellationToken).ConfigureAwait(false);
        }
    }

    public int UsageCount(string profileId)
        => workspace.Current.Connections.Count(c => c.TlsProfileId == profileId);

    public IReadOnlyList<string> ReferencingConnections(string profileId)
        => workspace.Current.Connections.Where(c => c.TlsProfileId == profileId).Select(c => c.Name).ToList();
}
