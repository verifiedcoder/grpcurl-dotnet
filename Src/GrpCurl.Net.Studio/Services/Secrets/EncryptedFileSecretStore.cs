using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GrpCurl.Net.Studio.ViewModels.Services;

namespace GrpCurl.Net.Studio.Services.Secrets;

/// <summary>
///     AES-256-GCM encrypted-file secret store: the cross-platform fallback used when no OS keychain is
///     available — e.g. a headless CI box or a minimal desktop with no Secret Service (SEC-023). The data
///     key is never stored: it is derived per session via HKDF-SHA256 from a machine + user-scoped input
///     (the machine id, the user id, and a random per-install salt kept mode-0600 next to the data). This
///     binds the ciphertext to this machine + user, but the derivation inputs sit on the same disk, so it is
///     weaker than a native keychain — the honest limitation in <see cref="Info" /> says so (SEC-024).
/// </summary>
internal sealed class EncryptedFileSecretStore : ISecretStore
{
    /// <summary>SEC-024: the verbatim honest-limitation text surfaced in Settings → Security.</summary>
    internal const string FallbackLimitation =
        "Secrets are stored in an encrypted file because no OS keychain (Secret Service / Keychain / DPAPI) "
        + "was available. This protects against casual disk reads and access by other users on this machine, "
        + "but NOT against an attacker running as you — the key-derivation inputs (a per-install salt and the "
        + "machine id) sit on the same disk as the encrypted data. For stronger protection, install or enable a "
        + "Secret Service provider such as GNOME Keyring or KWallet (with the Secret Service bridge).";

    private static readonly byte[] HkdfInfo = "GrpCurl.Net Studio secret store v1"u8.ToArray();

    private readonly string _directory;
    private readonly string _dataPath;
    private readonly string _saltPath;
    private readonly string _machineIdPath;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private byte[]? _key;

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
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var store = Load();
            store[keyRef] = Encrypt(value, Key());
            Save(store);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<string?> GetAsync(string keyRef, CancellationToken cancellationToken = default)
    {
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
            _gate.Release();
        }
    }

    public async Task DeleteAsync(string keyRef, CancellationToken cancellationToken = default)
    {
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
            _gate.Release();
        }
    }

    public async Task<bool> ExistsAsync(string keyRef, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return Load().ContainsKey(keyRef);
        }
        finally
        {
            _gate.Release();
        }
    }

    private Dictionary<string, string> Load()
        => File.Exists(_dataPath)
            ? JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(_dataPath)) ?? []
            : [];

    private void Save(Dictionary<string, string> store)
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(_dataPath, JsonSerializer.Serialize(store));
        Harden(_dataPath);
    }

    // ── SEC-023: HKDF-SHA256(ikm = machineId ∥ uid, salt = per-install random, info = label) ──

    private byte[] Key() => _key ??= HKDF.DeriveKey(
        HashAlgorithmName.SHA256,
        ikm: [.. MachineId(), .. UserScope()],
        outputLength: 32,
        salt: LoadOrCreateSalt(),
        info: HkdfInfo);

    private byte[] LoadOrCreateSalt()
    {
        if (File.Exists(_saltPath))
        {
            return Convert.FromBase64String(File.ReadAllText(_saltPath));
        }

        Directory.CreateDirectory(_directory);
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

        Directory.CreateDirectory(_directory);
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
}
