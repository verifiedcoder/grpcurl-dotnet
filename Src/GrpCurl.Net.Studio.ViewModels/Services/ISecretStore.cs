namespace GrpCurl.Net.Studio.ViewModels.Services;

/// <summary>
///     Stores secret-typed values (PKCS12 passwords, secret environment variables) exclusively, keyed by an
///     opaque namespaced keyref (<c>studio/v1/{scope}/...</c>, SPEC-040 §4 / SEC-020). The workspace, history,
///     and settings files only ever carry the keyref as <c>{"$secret":"&lt;keyRef&gt;"}</c>, never the value.
///     Backed by the OS keychain where available (Windows DPAPI, macOS Keychain, Linux Secret Service) with an
///     encrypted-file fallback (SEC-021..025); the live backend is reported via <see cref="Info" />.
/// </summary>
public interface ISecretStore : ISecretBackend
{
    /// <summary>
    ///     SEC-027: every keyref this store holds (names only, never values), for the Settings → Security
    ///     audit panel. Enumeration is owned by the router (which keeps a keyref index), not the individual
    ///     backends — native keychains (macOS Keychain, Linux Secret Service) have no portable list API.
    /// </summary>
    Task<IReadOnlyList<string>> ListAsync(CancellationToken cancellationToken = default);
}

/// <summary>
///     A single secret backend (one of: Windows DPAPI, macOS Keychain, Linux Secret Service, encrypted-file
///     fallback). Stores/retrieves/removes secret values by keyref; enumeration is deliberately absent here —
///     the <see cref="ISecretStore" /> router provides it from its own keyref index.
/// </summary>
public interface ISecretBackend
{
    Task SetAsync(string keyRef, string value, CancellationToken cancellationToken = default);

    Task<string?> GetAsync(string keyRef, CancellationToken cancellationToken = default);

    Task DeleteAsync(string keyRef, CancellationToken cancellationToken = default);

    /// <summary>Whether a value is stored for <paramref name="keyRef" /> (SPEC-050 §3.1).</summary>
    Task<bool> ExistsAsync(string keyRef, CancellationToken cancellationToken = default);

    /// <summary>The live backend, for the Settings → Security panel (SEC-024).</summary>
    SecretStoreInfo Info { get; }
}

/// <summary>
///     Describes the active <see cref="ISecretStore" /> backend (SPEC-050 §3.1) for display in
///     Settings → Security. <see cref="IsOsKeychain" /> is <see langword="false" /> only for the
///     encrypted-file fallback, which additionally carries the verbatim honest-limitation
///     <see cref="LimitationNote" /> (SEC-024).
/// </summary>
public sealed record SecretStoreInfo(string BackendName, bool IsOsKeychain, string? LimitationNote);
