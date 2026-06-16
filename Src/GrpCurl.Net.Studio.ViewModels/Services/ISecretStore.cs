namespace GrpCurl.Net.Studio.ViewModels.Services;

/// <summary>
///     Per-OS secret storage (ADR-009 / SEC-017): the PKCS12 password is the only TLS secret Studio
///     keeps, stored here and referenced from the workspace JSON as <c>{"$secret":"&lt;keyRef&gt;"}</c> —
///     never as a literal. The <paramref name="keyRef" /> is an opaque, caller-owned handle (a GUID),
///     so re-saving a profile overwrites the same entry. Backends: Windows DPAPI, Linux libsecret,
///     macOS Keychain, with an encrypted-file fallback where the native store is unavailable.
/// </summary>
public interface ISecretStore
{
    Task SetAsync(string keyRef, string value, CancellationToken cancellationToken = default);

    Task<string?> GetAsync(string keyRef, CancellationToken cancellationToken = default);

    Task DeleteAsync(string keyRef, CancellationToken cancellationToken = default);
}
