using GrpCurl.Net.Studio.ViewModels.Models.Connections;
using GrpCurl.Net.Studio.ViewModels.Services;

namespace GrpCurl.Net.Studio.Services;

/// <summary>
///     Default <see cref="ITlsProfileResolver" />. Looks the referenced profile up in the live
///     workspace (<see cref="IWorkspaceStore.Current" />) and, when the profile names a PKCS12
///     password secret, fetches it from <see cref="ISecretStore" /> at call time — the literal never
///     touches the workspace JSON (SEC-017).
/// </summary>
internal sealed class TlsProfileResolver(IWorkspaceStore workspace, ISecretStore secrets, WorkspaceFileAccess? fileAccess = null) : ITlsProfileResolver
{
    public async Task<(TlsProfile? Profile, string? Password)> ResolveAsync(
        SavedConnection connection, CancellationToken cancellationToken = default)
    {
        // Also guards descriptor-only connections before any secret is resolved.
        if (fileAccess is not null) { _ = ((JsonWorkspaceStore)workspace).AuthorizeConnection(connection); }
        // TLS material is meaningless for a plaintext target.
        if (connection.Transport != TransportMode.Tls || string.IsNullOrWhiteSpace(connection.TlsProfileId))
        {
            return (null, null);
        }

        // A dangling reference falls back to system-default validation rather than failing the call;
        // the profile editor (PR-C) keeps references intact in the UI.
        var profile = workspace.Current.TlsProfiles.FirstOrDefault(p => p.Id == connection.TlsProfileId);

        if (profile is null)
        {
            return (null, null);
        }

        if (string.IsNullOrWhiteSpace(profile.ClientCertPasswordSecretRef))
        {
            return (profile, null);
        }

        var password = await secrets.GetAsync(profile.ClientCertPasswordSecretRef, cancellationToken).ConfigureAwait(false);
        return (profile, password);
    }
}
