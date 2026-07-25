using GrpCurl.Net.Studio.ViewModels.Services;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;

namespace GrpCurl.Net.Studio.Services.Secrets;

/// <summary>
///     macOS secret backend over the login Keychain via direct Security.framework interop
///     (<see cref="SecurityFrameworkInterop" />: <c>SecItemAdd</c>/<c>SecItemUpdate</c>/
///     <c>SecItemCopyMatching</c>/<c>SecItemDelete</c>, generic passwords). The secret value only ever
///     exists as in-process bytes — never as a process argument or environment variable (PRD-001) — and
///     the managed copy is zeroed as soon as it is no longer needed. A locked keychain or denied
///     Keychain access throws <see cref="InvalidOperationException" /> so the facade falls back to the
///     encrypted-file store, matching every other native backend's failure contract.
/// </summary>
[SupportedOSPlatform("macos")]
internal sealed class MacKeychainSecretStore : ISecretBackend
{
    private const string Service = "GrpCurl.Net Studio";

    public SecretStoreInfo Info { get; } = new("macOS Keychain", IsOsKeychain: true, LimitationNote: null);

    public async Task<bool> ExistsAsync(string keyRef, CancellationToken cancellationToken = default)
        => await GetAsync(keyRef, cancellationToken).ConfigureAwait(false) is not null;

    public Task SetAsync(string keyRef, string value, CancellationToken cancellationToken = default)
    {
        // Keychain calls are synchronous, in-process, and not cancellable mid-flight; only the
        // not-yet-started case is honored.
        cancellationToken.ThrowIfCancellationRequested();

        var utf8 = Encoding.UTF8.GetBytes(value);
        try
        {
            // Upsert updates an existing item instead of failing on duplicate (previously the CLI's -U).
            var status = SecurityFrameworkInterop.Upsert(Service, keyRef, utf8);
            if (status != KeychainStatusMapping.ErrSecSuccess)
            {
                throw KeychainStatusMapping.ToException(status, "add/update");
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(utf8);
        }

        return Task.CompletedTask;
    }

    public Task<string?> GetAsync(string keyRef, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var status = SecurityFrameworkInterop.TryFind(Service, keyRef, out var utf8);
        if (status == KeychainStatusMapping.ErrSecItemNotFound)
        {
            return Task.FromResult<string?>(null);
        }

        if (status != KeychainStatusMapping.ErrSecSuccess || utf8 is null)
        {
            throw KeychainStatusMapping.ToException(status, "find");
        }

        try
        {
            return Task.FromResult<string?>(Encoding.UTF8.GetString(utf8));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(utf8);
        }
    }

    public Task DeleteAsync(string keyRef, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // Idempotent: deleting an already-absent item is not a failure (matches the previous
        // implementation, which ignored the CLI's exit code here).
        var status = SecurityFrameworkInterop.Delete(Service, keyRef);
        if (status != KeychainStatusMapping.ErrSecSuccess && status != KeychainStatusMapping.ErrSecItemNotFound)
        {
            throw KeychainStatusMapping.ToException(status, "delete");
        }

        return Task.CompletedTask;
    }
}
