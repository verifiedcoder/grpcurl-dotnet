using System.ComponentModel;
using System.Text.Json;
using GrpCurl.Net.Studio.ViewModels.Services;

namespace GrpCurl.Net.Studio.Services.Secrets;

/// <summary>
///     Default <see cref="ISecretStore" /> (SEC-020..025): selects a backend once at startup — the per-OS
///     native keystore (Windows DPAPI, macOS Keychain, Linux Secret Service) if a probe shows it is
///     available, otherwise the encrypted-file fallback. The selection is logged (backend name only) and
///     surfaced via <see cref="Info" /> for Settings → Security. If the native backend later fails mid-session
///     it transparently switches to the fallback (defence in depth); migration of already-stored secrets
///     between backends is out of scope for v1 (SEC-025) — a missed keyref means the user re-enters the value.
/// </summary>
internal sealed class SecretStore : ISecretStore
{
    private const string ProbeKeyRef = "studio/v1/app/probe";
    private const string IndexFileName = "secret-keyrefs.json";

    private readonly ISecretBackend _fallback;
    private readonly string _indexPath;
    private readonly SemaphoreSlim _indexGate = new(1, 1);

    private volatile ISecretBackend _active;
    private volatile bool _activeIsNative;

    public SecretStore(string directory, Action<string>? log = null)
    {
        _fallback = new EncryptedFileSecretStore(directory);
        _indexPath = Path.Combine(directory, IndexFileName);

        var native = OperatingSystem.IsWindows() ? new WindowsDpapiSecretStore(directory)
            : OperatingSystem.IsMacOS() ? (ISecretBackend?)new MacKeychainSecretStore()
            : OperatingSystem.IsLinux() ? new LinuxLibsecretSecretStore()
            : null;

        // SEC-025: probe the native backend once at startup; commit to native or fallback now.
        if (native is not null && Probe(native))
        {
            _active = native;
            _activeIsNative = true;
        }
        else
        {
            _active = _fallback;
            _activeIsNative = false;
        }

        log?.Invoke($"Secret store backend: {_active.Info.BackendName}");
    }

    public SecretStoreInfo Info => _active.Info;

    public async Task SetAsync(string keyRef, string value, CancellationToken cancellationToken = default)
    {
        await RunAsync(store => store.SetAsync(keyRef, value, cancellationToken)).ConfigureAwait(false);
        await UpdateIndexAsync(keyRef, present: true, cancellationToken).ConfigureAwait(false);
    }

    public Task<string?> GetAsync(string keyRef, CancellationToken cancellationToken = default)
        => RunAsync(store => store.GetAsync(keyRef, cancellationToken));

    public async Task DeleteAsync(string keyRef, CancellationToken cancellationToken = default)
    {
        await RunAsync(store => store.DeleteAsync(keyRef, cancellationToken)).ConfigureAwait(false);
        await UpdateIndexAsync(keyRef, present: false, cancellationToken).ConfigureAwait(false);
    }

    public Task<bool> ExistsAsync(string keyRef, CancellationToken cancellationToken = default)
        => RunAsync(store => store.ExistsAsync(keyRef, cancellationToken));

    // SEC-027: enumeration is served from the router's keyref index, kept in sync on every set/delete — the
    // native keychains cannot be portably listed, so the index is the single source of truth across backends.
    public async Task<IReadOnlyList<string>> ListAsync(CancellationToken cancellationToken = default)
    {
        await _indexGate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            return ReadIndex();
        }
        finally
        {
            _indexGate.Release();
        }
    }

    private async Task UpdateIndexAsync(string keyRef, bool present, CancellationToken cancellationToken)
    {
        await _indexGate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            var keys = ReadIndex();
            var changed = present ? (!keys.Contains(keyRef) && Add(keys, keyRef)) : keys.Remove(keyRef);

            if (changed)
            {
                WriteIndex(keys);
            }
        }
        finally
        {
            _indexGate.Release();
        }

        static bool Add(List<string> keys, string keyRef)
        {
            keys.Add(keyRef);
            return true;
        }
    }

    private List<string> ReadIndex()
    {
        try
        {
            return File.Exists(_indexPath)
                ? JsonSerializer.Deserialize<List<string>>(File.ReadAllText(_indexPath)) ?? []
                : [];
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            return []; // a corrupt index reads as empty; the next set/delete rewrites it
        }
    }

    private void WriteIndex(List<string> keys)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_indexPath)!);
        var tempPath = _indexPath + ".tmp";
        File.WriteAllText(tempPath, JsonSerializer.Serialize(keys));
        File.Move(tempPath, _indexPath, overwrite: true);
    }

    private static bool Probe(ISecretBackend native)
    {
        try
        {
            // A lookup of an absent sentinel is non-interactive: it returns false on a working backend and
            // throws only when the backend itself is unavailable (no library, no D-Bus, locked).
            native.ExistsAsync(ProbeKeyRef).GetAwaiter().GetResult();
            return true;
        }
        catch (Exception ex) when (IsNativeFailure(ex))
        {
            return false;
        }
    }

    private async Task RunAsync(Func<ISecretBackend, Task> operation)
    {
        if (!_activeIsNative)
        {
            await operation(_active).ConfigureAwait(false);
            return;
        }

        try
        {
            await operation(_active).ConfigureAwait(false);
        }
        catch (Exception ex) when (IsNativeFailure(ex))
        {
            DemoteToFallback();
            await operation(_fallback).ConfigureAwait(false);
        }
    }

    private async Task<T> RunAsync<T>(Func<ISecretBackend, Task<T>> operation)
    {
        if (!_activeIsNative)
        {
            return await operation(_active).ConfigureAwait(false);
        }

        try
        {
            return await operation(_active).ConfigureAwait(false);
        }
        catch (Exception ex) when (IsNativeFailure(ex))
        {
            DemoteToFallback();
            return await operation(_fallback).ConfigureAwait(false);
        }
    }

    private void DemoteToFallback()
    {
        _active = _fallback;
        _activeIsNative = false;
    }

    private static bool IsNativeFailure(Exception ex) => ex
        is DllNotFoundException
        or EntryPointNotFoundException
        or InvalidOperationException
        or Win32Exception
        or TypeInitializationException
        or PlatformNotSupportedException;
}
