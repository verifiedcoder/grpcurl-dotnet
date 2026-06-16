using GrpCurl.Net.Studio.ViewModels.Models.Connections;

namespace GrpCurl.Net.Studio.Services;

/// <summary>
///     Resolves the <see cref="TlsProfile" /> a connection references (FR-012/FR-030) and the one
///     secret that lives outside the workspace file — the PKCS12 password (SEC-017), fetched from
///     <see cref="ViewModels.Services.ISecretStore" />. Channel building stays in
///     <see cref="ConnectionChannelMapper" />; this only turns a <see cref="SavedConnection" /> into the
///     (profile, password) pair the mapper consumes. Returns <c>(null, null)</c> when the connection is
///     plaintext, references no profile, or the referenced profile no longer exists — in every such case
///     the connection uses system-default TLS validation.
/// </summary>
internal interface ITlsProfileResolver
{
    Task<(TlsProfile? Profile, string? Password)> ResolveAsync(
        SavedConnection connection, CancellationToken cancellationToken = default);
}
