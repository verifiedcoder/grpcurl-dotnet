using GrpCurl.Net.Studio.ViewModels.Services;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace GrpCurl.Net.Studio.Services.Secrets;

/// <summary>
///     Windows secret backend: each value is DPAPI-protected (<see cref="DataProtectionScope.CurrentUser" />)
///     and the ciphertext stored in a per-user JSON file. DPAPI ties decryption to the logged-in user
///     without a key file, and works headlessly (no keyring service required).
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class WindowsDpapiSecretStore : ISecretBackend, IDisposable
{
    /// <summary>
    ///     How long disposal waits for an in-flight operation to leave the critical section before
    ///     giving up on draining. Bounded on purpose: shutdown must not hang on a stuck operation.
    /// </summary>
    private static readonly TimeSpan DisposeDrainTimeout = TimeSpan.FromSeconds(2);

    private readonly string _dataPath;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private int _disposed;

    public WindowsDpapiSecretStore(string directory) => _dataPath = Path.Combine(directory, "secrets.dpapi.json");

    public SecretStoreInfo Info { get; } = new("Windows DPAPI", IsOsKeychain: true, LimitationNote: null);

    public async Task<bool> ExistsAsync(string keyRef, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return Load().ContainsKey(keyRef);
        }
        finally
        {
            _ = _gate.Release();
        }
    }

    public async Task SetAsync(string keyRef, string value, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var store = Load();
            var protectedBytes = ProtectedData.Protect(Encoding.UTF8.GetBytes(value), optionalEntropy: null, DataProtectionScope.CurrentUser);
            store[keyRef] = Convert.ToBase64String(protectedBytes);
            Save(store);
        }
        finally
        {
            _ = _gate.Release();
        }
    }

    public async Task<string?> GetAsync(string keyRef, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!Load().TryGetValue(keyRef, out var b64))
            {
                return null;
            }

            var bytes = ProtectedData.Unprotect(Convert.FromBase64String(b64), optionalEntropy: null, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(bytes);
        }
        finally
        {
            _ = _gate.Release();
        }
    }

    public async Task DeleteAsync(string keyRef, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var store = Load();
            if (store.Remove(keyRef))
            {
                Save(store);
            }
        }
        finally
        {
            _ = _gate.Release();
        }
    }

    private Dictionary<string, string> Load()
        => File.Exists(_dataPath)
            ? JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(_dataPath)) ?? []
            : [];

    private void Save(Dictionary<string, string> store)
    {
        _ = Directory.CreateDirectory(Path.GetDirectoryName(_dataPath)!);
        File.WriteAllText(_dataPath, JsonSerializer.Serialize(store));
    }

    private void ThrowIfDisposed()
        => ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);

    /// <summary>
    ///     Releases the write gate. Idempotent and non-throwing (PRD-005). Reachable only since
    ///     <see cref="SecretStore" /> started disposing the backend it owns — before that its own
    ///     <c>Dispose</c> threw first, so this one never ran.
    /// </summary>
    public void Dispose()
    {
        // Atomic, so two concurrent disposals cannot both pass the check and race the teardown.
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        // Drain before destroying the gate: disposing a SemaphoreSlim while an operation owns it (or is
        // queued on it) is undefined, and the previous version simply assumed no work was live
        // (PRD-005 review, finding 3). Bounded, so shutdown cannot hang.
        var drained = _gate.Wait(DisposeDrainTimeout);

        // Disposed while still held, so nothing can queue behind it; a later caller gets
        // ObjectDisposedException. If the drain timed out the gate is left alone rather than destroyed
        // under an owner.
        if (drained)
        {
            _gate.Dispose();
        }
    }
}
