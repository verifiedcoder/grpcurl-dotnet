using System.ComponentModel;
using GrpCurl.Net.Studio.ViewModels.Services;

namespace GrpCurl.Net.Studio.Services.Secrets;

/// <summary>
///     Default <see cref="ISecretStore" /> (SEC-017): routes to the per-OS native keystore — Windows
///     DPAPI, Linux libsecret, macOS Keychain — and transparently falls back to the encrypted-file
///     store the first time the native backend is unavailable (no keyring/D-Bus, locked keychain,
///     missing library). The fallback keeps the app fully functional, including on headless CI.
/// </summary>
internal sealed class SecretStore : ISecretStore
{
    private readonly ISecretStore _native;
    private readonly ISecretStore _fallback;
    private volatile bool _nativeBroken;

    public SecretStore(string directory)
    {
        _fallback = new EncryptedFileSecretStore(directory);
        _native = OperatingSystem.IsWindows() ? new WindowsDpapiSecretStore(directory)
            : OperatingSystem.IsLinux() ? new LinuxLibsecretSecretStore()
            : OperatingSystem.IsMacOS() ? new MacKeychainSecretStore()
            : _fallback;
    }

    public Task SetAsync(string keyRef, string value, CancellationToken cancellationToken = default)
        => RunAsync(store => store.SetAsync(keyRef, value, cancellationToken));

    public Task<string?> GetAsync(string keyRef, CancellationToken cancellationToken = default)
        => RunAsync(store => store.GetAsync(keyRef, cancellationToken));

    public Task DeleteAsync(string keyRef, CancellationToken cancellationToken = default)
        => RunAsync(store => store.DeleteAsync(keyRef, cancellationToken));

    private async Task RunAsync(Func<ISecretStore, Task> operation)
    {
        if (_nativeBroken)
        {
            await operation(_fallback).ConfigureAwait(false);
            return;
        }

        try
        {
            await operation(_native).ConfigureAwait(false);
        }
        catch (Exception ex) when (IsNativeFailure(ex))
        {
            _nativeBroken = true;
            await operation(_fallback).ConfigureAwait(false);
        }
    }

    private async Task<T> RunAsync<T>(Func<ISecretStore, Task<T>> operation)
    {
        if (_nativeBroken)
        {
            return await operation(_fallback).ConfigureAwait(false);
        }

        try
        {
            return await operation(_native).ConfigureAwait(false);
        }
        catch (Exception ex) when (IsNativeFailure(ex))
        {
            _nativeBroken = true;
            return await operation(_fallback).ConfigureAwait(false);
        }
    }

    private static bool IsNativeFailure(Exception ex) => ex
        is DllNotFoundException
        or EntryPointNotFoundException
        or InvalidOperationException
        or Win32Exception
        or TypeInitializationException
        or PlatformNotSupportedException;
}
