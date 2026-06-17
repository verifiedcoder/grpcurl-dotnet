using System.ComponentModel;
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

    private readonly ISecretStore _fallback;

    private volatile ISecretStore _active;
    private volatile bool _activeIsNative;

    public SecretStore(string directory, Action<string>? log = null)
    {
        _fallback = new EncryptedFileSecretStore(directory);

        var native = OperatingSystem.IsWindows() ? new WindowsDpapiSecretStore(directory)
            : OperatingSystem.IsMacOS() ? (ISecretStore?)new MacKeychainSecretStore()
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

    public Task SetAsync(string keyRef, string value, CancellationToken cancellationToken = default)
        => RunAsync(store => store.SetAsync(keyRef, value, cancellationToken));

    public Task<string?> GetAsync(string keyRef, CancellationToken cancellationToken = default)
        => RunAsync(store => store.GetAsync(keyRef, cancellationToken));

    public Task DeleteAsync(string keyRef, CancellationToken cancellationToken = default)
        => RunAsync(store => store.DeleteAsync(keyRef, cancellationToken));

    public Task<bool> ExistsAsync(string keyRef, CancellationToken cancellationToken = default)
        => RunAsync(store => store.ExistsAsync(keyRef, cancellationToken));

    private static bool Probe(ISecretStore native)
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

    private async Task RunAsync(Func<ISecretStore, Task> operation)
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

    private async Task<T> RunAsync<T>(Func<ISecretStore, Task<T>> operation)
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
