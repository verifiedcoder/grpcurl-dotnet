using GrpCurl.Net.Studio.ViewModels.Services;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace GrpCurl.Net.Studio.Services.Secrets;

/// <summary>
///     AES-256-GCM encrypted-file secret store: the cross-platform fallback used when no OS keychain is
///     available — e.g. a headless CI box or a minimal desktop with no Secret Service (SEC-023). The data
///     key is never stored: it is derived per session via HKDF-SHA256 from a machine + user-scoped input
///     (the machine id, the user id, and a random per-install salt kept mode-0600 next to the data). This
///     binds the ciphertext to this machine + user, but the derivation inputs sit on the same disk, so it is
///     weaker than a native keychain — the honest limitation in <see cref="Info" /> says so (SEC-024).
/// </summary>
internal sealed class EncryptedFileSecretStore : ISecretBackend, IDisposable
{
    /// <summary>SEC-024: the verbatim honest-limitation text surfaced in Settings → Security.</summary>
    internal const string FallbackLimitation =
        "Secrets are stored in an encrypted file because no OS keychain (Secret Service / Keychain / DPAPI) "
        + "was available. This protects against casual disk reads and access by other users on this machine, "
        + "but NOT against an attacker running as you — the key-derivation inputs (a per-install salt and the "
        + "machine id) sit on the same disk as the encrypted data. For stronger protection, install or enable a "
        + "Secret Service provider such as GNOME Keyring or KWallet (with the Secret Service bridge).";

    /// <summary>
    ///     How long disposal waits for an in-flight operation to leave the critical section before
    ///     giving up on draining. Bounded on purpose: shutdown must not hang on a stuck operation, and
    ///     a straggler is rejected by the disposed check rather than by a destroyed gate.
    /// </summary>
    private static readonly TimeSpan DisposeDrainTimeout = TimeSpan.FromSeconds(2);

    private static readonly byte[] HkdfInfo = "GrpCurl.Net Studio secret store v1"u8.ToArray();

    private readonly string _directory;
    private readonly string _dataPath;
    private readonly string _saltPath;
    private readonly string _machineIdPath;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private byte[]? _key;
    private int _disposed;

    /// <summary>
    ///     The cached derived key, for the PRD-005 zeroization test only. A test cannot otherwise
    ///     observe that <see cref="Dispose" /> cleared the buffer rather than merely dropping the
    ///     reference, and asserting that is the point of the test. Read-only, and on no code path the
    ///     product takes.
    /// </summary>
    internal byte[]? KeyForTests => _key;

    public EncryptedFileSecretStore(string directory)
    {
        _directory = directory;
        _dataPath = Path.Combine(directory, "secrets.json");
        _saltPath = Path.Combine(directory, "secrets.salt");
        _machineIdPath = Path.Combine(directory, "machine.id");
    }

    public SecretStoreInfo Info { get; } = new("Encrypted file (fallback)", IsOsKeychain: false, FallbackLimitation);

    public async Task SetAsync(string keyRef, string value, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var store = Load();
            store[keyRef] = Encrypt(value, Key());
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
            return Load().TryGetValue(keyRef, out var blob) ? Decrypt(blob, Key()) : null;
        }
        catch (CryptographicException)
        {
            // A blob written under a different key (e.g. backend changed; SEC-025 says re-enter, no migration).
            return null;
        }
        catch (FormatException)
        {
            return null;
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

    private Dictionary<string, string> Load()
        => File.Exists(_dataPath)
            ? JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(_dataPath)) ?? []
            : [];

    private void Save(Dictionary<string, string> store)
    {
        _ = Directory.CreateDirectory(_directory);
        File.WriteAllText(_dataPath, JsonSerializer.Serialize(store));
        Harden(_dataPath);
    }

    // ── SEC-023: HKDF-SHA256(ikm = machineId ∥ uid, salt = per-install random, info = label) ──

    private void ThrowIfDisposed()
        => ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);

    private byte[] Key()
    {
        // Also checked here, not only at the entry points: if the drain timed out, an operation that
        // owned the gate is still running and must not derive or reuse a key that has been zeroed.
        ThrowIfDisposed();

        return _key ??= HKDF.DeriveKey(
            HashAlgorithmName.SHA256,
            ikm: [.. MachineId(), .. UserScope()],
            outputLength: 32,
            salt: LoadOrCreateSalt(),
            info: HkdfInfo);
    }

    private byte[] LoadOrCreateSalt()
    {
        if (File.Exists(_saltPath))
        {
            return Convert.FromBase64String(File.ReadAllText(_saltPath));
        }

        _ = Directory.CreateDirectory(_directory);
        var salt = RandomNumberGenerator.GetBytes(32);
        File.WriteAllText(_saltPath, Convert.ToBase64String(salt));
        Harden(_saltPath);
        return salt;
    }

    /// <summary>The host machine id: <c>/etc/machine-id</c> (or the dbus copy) on Linux, else a persisted random id.</summary>
    private byte[] MachineId()
    {
        foreach (var path in (ReadOnlySpan<string>)["/etc/machine-id", "/var/lib/dbus/machine-id"])
        {
            if (File.Exists(path))
            {
                var id = File.ReadAllText(path).Trim();
                if (id.Length > 0)
                {
                    return Encoding.UTF8.GetBytes(id);
                }
            }
        }

        if (File.Exists(_machineIdPath))
        {
            return Encoding.UTF8.GetBytes(File.ReadAllText(_machineIdPath).Trim());
        }

        _ = Directory.CreateDirectory(_directory);
        var generated = Guid.NewGuid().ToString("N");
        File.WriteAllText(_machineIdPath, generated);
        Harden(_machineIdPath);
        return Encoding.UTF8.GetBytes(generated);
    }

    /// <summary>The user scope: the POSIX uid where available, else the user name.</summary>
    private static byte[] UserScope()
    {
        if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
        {
            try
            {
                return BitConverter.GetBytes(getuid());
            }
            catch (DllNotFoundException)
            {
                // Fall through to the user name.
            }
            catch (EntryPointNotFoundException)
            {
            }
        }

        return Encoding.UTF8.GetBytes(Environment.UserName);
    }

    private static void Harden(string path)
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        try
        {
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
        catch (IOException)
        {
            // Best-effort hardening.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static string Encrypt(string value, byte[] key)
    {
        var plaintext = Encoding.UTF8.GetBytes(value);
        var nonce = RandomNumberGenerator.GetBytes(AesGcm.NonceByteSizes.MaxSize);
        var cipher = new byte[plaintext.Length];
        var tag = new byte[AesGcm.TagByteSizes.MaxSize];

        using var aes = new AesGcm(key, tag.Length);
        aes.Encrypt(nonce, plaintext, cipher, tag);

        return $"{Convert.ToBase64String(nonce)}.{Convert.ToBase64String(tag)}.{Convert.ToBase64String(cipher)}";
    }

    private static string Decrypt(string blob, byte[] key)
    {
        var parts = blob.Split('.');
        if (parts.Length != 3)
        {
            throw new FormatException("Malformed secret blob.");
        }

        var nonce = Convert.FromBase64String(parts[0]);
        var tag = Convert.FromBase64String(parts[1]);
        var cipher = Convert.FromBase64String(parts[2]);
        var plaintext = new byte[cipher.Length];

        using var aes = new AesGcm(key, tag.Length);
        aes.Decrypt(nonce, cipher, tag, plaintext);

        return Encoding.UTF8.GetString(plaintext);
    }

    [DllImport("libc")]
    private static extern uint getuid();

    /// <summary>
    ///     Zeroes the derived key, then releases the write gate. Idempotent and non-throwing (PRD-005).
    ///     <para>
    ///         The key is the reason this one matters beyond shutdown hygiene: it is an HKDF-derived
    ///         AES-256 key cached for the process lifetime, and without this it stayed readable in the
    ///         managed heap until exit. <see cref="CryptographicOperations.ZeroMemory" /> is used rather
    ///         than dropping the reference so the bytes are gone rather than merely unreachable.
    ///     </para>
    ///     Ordering: zero before releasing the gate, so a caller that somehow raced this far still
    ///     serialises against the crypto operations rather than observing a half-cleared key.
    /// </summary>
    public void Dispose()
    {
        // Atomic, so two concurrent disposals cannot both pass the check and race the teardown below,
        // and so every operation entry point sees the rejection at the same instant.
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        // Drain before destroying anything. The previous version zeroed the key and disposed the gate
        // without ever acquiring it, so an operation already inside the critical section could have the
        // key cleared underneath its AES call and then throw from its own Release() (PRD-005 review,
        // finding 3). Bounded, because shutdown must not hang on a stuck operation.
        var drained = _gate.Wait(DisposeDrainTimeout);

        try
        {
            if (_key is not null)
            {
                CryptographicOperations.ZeroMemory(_key);

                _key = null;
            }
        }
        finally
        {
            // Disposed while still held, so nothing can queue behind it; a caller that arrives later
            // gets ObjectDisposedException, which is the pinned behaviour. If the drain timed out we
            // leave the gate alone rather than disposing it under an owner — the undefined case.
            if (drained)
            {
                _gate.Dispose();
            }
        }
    }
}
