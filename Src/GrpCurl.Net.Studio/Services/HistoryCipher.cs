using GrpCurl.Net.Studio.ViewModels.Services;
using System.Security.Cryptography;
using System.Text;

namespace GrpCurl.Net.Studio.Services;

internal sealed class HistoryCipher(ISecretStore secrets, Action<string, byte[]>? observe = null)
{
    internal const string KeyReference = "studio/v1/history/at-rest-key";
    internal const string Prefix = "enc1:";
    private static readonly byte[] AssociatedData = "GrpCurl.Net Studio history enc1"u8.ToArray();

    internal async Task CheckAccessAsync(CancellationToken token) => _ = await secrets.GetAsync(KeyReference, token).ConfigureAwait(false);

    internal async Task<byte[]> ResolveKeyAsync(bool mayCreate, CancellationToken token)
    {
        var encoded = await secrets.GetAsync(KeyReference, token).ConfigureAwait(false);
        if (encoded is null)
        {
            if (!mayCreate) { throw new HistoryStorageException("History encryption key is missing. Restore the original keystore and history backup, or explicitly clear all history. Existing history has been retained."); }
            var created = RandomNumberGenerator.GetBytes(32);
            try
            {
                observe?.Invoke("key", created);
                await secrets.SetAsync(KeyReference, Convert.ToBase64String(created), token).ConfigureAwait(false);
            }
            finally { CryptographicOperations.ZeroMemory(created); }
            // Re-read only the committed value, including after backend selection/consent checks.
            encoded = await secrets.GetAsync(KeyReference, token).ConfigureAwait(false);
        }
        var key = Convert.FromBase64String(encoded ?? throw new HistoryStorageException("History key could not be stored. Retry after restoring secret storage."));
        try
        {
            observe?.Invoke("key", key);
            if (key.Length != 32) { throw new FormatException(); }
            return key;
        }
        catch { CryptographicOperations.ZeroMemory(key); throw; }
    }

    internal static long EnvelopeLength(int plaintextBytes) => Prefix.Length + ((plaintextBytes + 28L + 2) / 3 * 4);

    internal string Protect(string line, byte[] key)
    {
        var nonce = RandomNumberGenerator.GetBytes(12);
        byte[] cipher, tag;
        var plain = Encoding.UTF8.GetBytes(line);
        try
        {
            observe?.Invoke("encrypt", plain);
            cipher = new byte[plain.Length];
            tag = new byte[16];
            using var aes = new AesGcm(key, 16);
            aes.Encrypt(nonce, plain, cipher, tag, AssociatedData);
        }
        finally { CryptographicOperations.ZeroMemory(plain); }
        return Prefix + Convert.ToBase64String([.. nonce, .. cipher, .. tag]);
    }

    internal string Unprotect(string line, byte[] key)
    {
        var blob = Convert.FromBase64String(line[Prefix.Length..]);
        if (blob.Length < 28) { throw new FormatException(); }
        var plain = new byte[blob.Length - 28];
        try
        {
            observe?.Invoke("decryptAllocated", plain);
            using var aes = new AesGcm(key, 16);
            aes.Decrypt(blob.AsSpan(0, 12), blob.AsSpan(12, plain.Length), blob.AsSpan(blob.Length - 16), plain, AssociatedData);
            observe?.Invoke("decrypt", plain);
            return Encoding.UTF8.GetString(plain);
        }
        finally { CryptographicOperations.ZeroMemory(plain); }
    }
}
