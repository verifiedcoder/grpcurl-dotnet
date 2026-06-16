using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GrpCurl.Net.Studio.ViewModels.Services;

namespace GrpCurl.Net.Studio.Services.Secrets;

/// <summary>
///     AES-GCM encrypted-file secret store: the cross-platform fallback used when a native keystore
///     (libsecret / Keychain) is unavailable — e.g. on a headless CI box with no keyring. The data
///     key lives in a sibling <c>secrets.key</c> file (0600 on Unix); this is weaker than a native
///     keystore (key sits next to the ciphertext) and is documented as a fallback, not the primary.
/// </summary>
internal sealed class EncryptedFileSecretStore : ISecretStore
{
    private readonly string _dataPath;
    private readonly string _keyPath;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public EncryptedFileSecretStore(string directory)
    {
        _dataPath = Path.Combine(directory, "secrets.json");
        _keyPath = Path.Combine(directory, "secrets.key");
    }

    public async Task SetAsync(string keyRef, string value, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var key = LoadOrCreateKey();
            var store = Load();
            store[keyRef] = Encrypt(value, key);
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
            if (!File.Exists(_keyPath) || !Load().TryGetValue(keyRef, out var blob))
            {
                return null;
            }

            return Decrypt(blob, LoadOrCreateKey());
        }
        catch (CryptographicException)
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

    private Dictionary<string, string> Load()
        => File.Exists(_dataPath)
            ? JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(_dataPath)) ?? []
            : [];

    private void Save(Dictionary<string, string> store)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_dataPath)!);
        File.WriteAllText(_dataPath, JsonSerializer.Serialize(store));
    }

    private byte[] LoadOrCreateKey()
    {
        if (File.Exists(_keyPath))
        {
            return Convert.FromBase64String(File.ReadAllText(_keyPath));
        }

        Directory.CreateDirectory(Path.GetDirectoryName(_keyPath)!);
        var key = RandomNumberGenerator.GetBytes(32);
        File.WriteAllText(_keyPath, Convert.ToBase64String(key));

        if (!OperatingSystem.IsWindows())
        {
            try
            {
                File.SetUnixFileMode(_keyPath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            }
            catch (IOException)
            {
                // Best-effort hardening.
            }
        }

        return key;
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
        var nonce = Convert.FromBase64String(parts[0]);
        var tag = Convert.FromBase64String(parts[1]);
        var cipher = Convert.FromBase64String(parts[2]);
        var plaintext = new byte[cipher.Length];

        using var aes = new AesGcm(key, tag.Length);
        aes.Decrypt(nonce, cipher, tag, plaintext);

        return Encoding.UTF8.GetString(plaintext);
    }
}
