using GrpCurl.Net.Studio.ViewModels.Services;
using System.Security.Cryptography;
using System.Text;

namespace GrpCurl.Net.Studio.Services.Secrets;

/// <summary>
///     macOS secret backend over the login Keychain via direct Security.framework interop
///     (<see cref="IKeychainNative" />, implemented by <see cref="SecurityFrameworkInterop" />:
///     <c>SecItemAdd</c>/<c>SecItemUpdate</c>/<c>SecItemCopyMatching</c>/<c>SecItemDelete</c>, generic
///     passwords). The secret value only ever exists as in-process bytes — never as a process argument or
///     environment variable (PRD-001) — and the managed copy is zeroed as soon as it is no longer needed.
///     The <c>SecItem</c> calls are documented as blocking (a slow/locked keychain or an ACL prompt can
///     take arbitrarily long), so each operation runs on a thread-pool thread rather than on the caller's
///     (UI) thread; only pre-start cancellation is honored, since a native Keychain call cannot be
///     cancelled once begun. A locked keychain or denied Keychain access throws
///     <see cref="InvalidOperationException" /> so the facade falls back to the encrypted-file store,
///     matching every other native backend's failure contract.
/// </summary>
internal sealed class MacKeychainSecretStore : ISecretBackend
{
    private const string Service = "GrpCurl.Net Studio";

    private readonly IKeychainNative _native;

    public MacKeychainSecretStore()
        : this(CreateDefaultNative())
    {
    }

    // Test seam: a fake IKeychainNative exercises the backend's encoding, zeroing, background execution,
    // and OSStatus→exception mapping on any OS, without a real Keychain.
    internal MacKeychainSecretStore(IKeychainNative native) => _native = native;

    public SecretStoreInfo Info { get; } = new("macOS Keychain", IsOsKeychain: true, LimitationNote: null);

    public async Task<bool> ExistsAsync(string keyRef, CancellationToken cancellationToken = default)
        => await GetAsync(keyRef, cancellationToken).ConfigureAwait(false) is not null;

    public Task SetAsync(string keyRef, string value, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return Task.Run(() =>
        {
            var utf8 = Encoding.UTF8.GetBytes(value);
            try
            {
                // Upsert updates an existing item instead of failing on duplicate (previously the CLI's -U).
                var status = _native.Upsert(Service, keyRef, utf8);
                if (status != KeychainStatusMapping.ErrSecSuccess)
                {
                    throw KeychainStatusMapping.ToException(status, "add/update");
                }
            }
            finally
            {
                CryptographicOperations.ZeroMemory(utf8);
            }
        }, cancellationToken);
    }

    public Task<string?> GetAsync(string keyRef, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return Task.Run<string?>(() =>
        {
            var status = _native.TryFind(Service, keyRef, out var utf8);
            if (status == KeychainStatusMapping.ErrSecItemNotFound)
            {
                return null;
            }

            if (status != KeychainStatusMapping.ErrSecSuccess || utf8 is null)
            {
                throw KeychainStatusMapping.ToException(status, "find");
            }

            try
            {
                return Encoding.UTF8.GetString(utf8);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(utf8);
            }
        }, cancellationToken);
    }

    public Task DeleteAsync(string keyRef, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return Task.Run(() =>
        {
            // Idempotent: deleting an already-absent item is not a failure (matches the previous
            // implementation, which ignored the CLI's exit code here).
            var status = _native.Delete(Service, keyRef);
            if (status != KeychainStatusMapping.ErrSecSuccess && status != KeychainStatusMapping.ErrSecItemNotFound)
            {
                throw KeychainStatusMapping.ToException(status, "delete");
            }
        }, cancellationToken);
    }

    private static IKeychainNative CreateDefaultNative()
    {
        // The if-guard (not the type's [SupportedOSPlatform] alone) is what the CA1416 analyzer recognizes,
        // so this method stays platform-clean while the real interop remains the single macOS-gated type.
        if (OperatingSystem.IsMacOS())
        {
            return new SecurityFrameworkInterop();
        }

        throw new PlatformNotSupportedException("The macOS Keychain backend is only available on macOS.");
    }
}
