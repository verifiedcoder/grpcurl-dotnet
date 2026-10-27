using GrpCurl.Net.Studio.ViewModels.Models;
using GrpCurl.Net.Studio.ViewModels.Services;
using System.ComponentModel;
using System.Security.Cryptography;
using System.Text.Json;

namespace GrpCurl.Net.Studio.Services.Secrets;

/// <summary>
///     Native storage is preferred. An unavailable native backend never causes an operation to be
///     retried against file storage. Fallback needs explicit user/admin authorization (PRD-006).
///     Once selected, a backend remains selected for the session; no implicit migration occurs.
/// </summary>
internal sealed class SecretStore : ISecretStore, ISecretStoreControl, IDisposable
{
    private const string ProbeKeyRef = "studio/v1/app/probe";
    private const string RecoveryHint = "Unlock your keychain or start a Secret Service provider, then retry. See Settings → Security.";
    private readonly ISecretBackend _fallback;
    private readonly ISecretBackend? _native;
    private readonly string _indexPath;
    private readonly ISettingsStore? _settings;
    private readonly SecretFallbackPolicy _policy;
    private readonly SecretFallbackMode _mode;
    private readonly Action<string>? _log;
    private readonly Action<string>? _warning;

    // These gates are only awaited (no native wait handle). Do not dispose them under admitted work.
    private readonly SemaphoreSlim _indexGate = new(1, 1);
    private readonly SemaphoreSlim _selectionGate = new(1, 1);
    private volatile ISecretBackend? _active;
    private volatile SecretStoreState _state = new(new("Unavailable", false, null), string.Empty, false, false,
        EncryptedFileSecretStore.FallbackLimitation);
    private int _disposed;
    private int _operations;
    private int _backendsReleased;

    public SecretStore(string directory, Action<string>? log = null, ISettingsStore? settings = null,
        Action<string>? warning = null)
        : this(directory, CreateNative(directory), new EncryptedFileSecretStore(directory),
            SecretFallbackPolicy.Load(SecretFallbackPolicy.DefaultPath), settings, log, warning)
    {
    }

    // The real constructor delegates to this same selection/ownership path. Tests supply deterministic
    // backends and a machine policy without changing private fields or touching the user's keychain.
    internal SecretStore(string directory, ISecretBackend? native, ISecretBackend fallback,
        SecretFallbackPolicy? policy = null, ISettingsStore? settings = null,
        Action<string>? log = null, Action<string>? warning = null)
    {
        _native = native;
        _fallback = fallback;
        _indexPath = Path.Combine(directory, "secret-keyrefs.json");
        _settings = settings;
        _policy = policy ?? new(SecretFallbackMode.RequireConsent);
        _log = log;
        _warning = warning ?? log;
        var userMode = settings?.Current.Security?.SecretFallback ?? SecretFallbackMode.RequireConsent;
        _mode = !Enum.IsDefined(userMode) || !Enum.IsDefined(_policy.Mode)
            || userMode == SecretFallbackMode.Block || _policy.Mode == SecretFallbackMode.Block
            ? SecretFallbackMode.Block
            : userMode == SecretFallbackMode.Allow || _policy.Mode == SecretFallbackMode.Allow
                ? SecretFallbackMode.Allow
                : SecretFallbackMode.RequireConsent;

        SelectNativeOrUnavailable();
        if (_active is null && State.CanEnableFallback && _mode == SecretFallbackMode.Allow)
        {
            SelectFallback(_policy.Mode == SecretFallbackMode.Allow ? "administrator policy" : "saved user consent");
        }
        _log?.Invoke($"Secret store backend: {Info.BackendName}");
    }

    private static ISecretBackend? CreateNative(string directory)
        => OperatingSystem.IsWindows() ? new WindowsDpapiSecretStore(directory)
            : OperatingSystem.IsMacOS() ? new MacKeychainSecretStore()
            : OperatingSystem.IsLinux() ? new LinuxLibsecretSecretStore()
            : null;

    public SecretStoreInfo Info => State.Info;
    public SecretStoreState State => _state;
    public event EventHandler? StateChanged;

    private void Publish(SecretStoreState state)
    {
        if (_state == state)
        {
            return;
        }
        _state = state;
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private void SelectNativeOrUnavailable()
    {
        var knownUnavailable = _native is null;
        try
        {
            if (_native is not null)
            {
                _ = _native.ExistsAsync(ProbeKeyRef).GetAwaiter().GetResult();
                _active = _native;
                Publish(new(_native.Info, "Secrets use the native keystore.", false, false,
                    EncryptedFileSecretStore.FallbackLimitation));
                return;
            }
        }
        catch (Exception ex) when (IsUnavailable(ex))
        {
            knownUnavailable = true;
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception
            or TypeInitializationException or PlatformNotSupportedException or CryptographicException
            or IOException or UnauthorizedAccessException)
        {
            // A functional/unknown error is not consent to downgrade. Keep Studio usable, but require
            // native recovery. Never include the exception text, which may carry sensitive native data.
            knownUnavailable = false;
        }

        var message = _policy.Problem ?? (_mode == SecretFallbackMode.Block
            ? "Encrypted-file fallback is blocked by policy or security settings. " + RecoveryHint
            : knownUnavailable
                ? "Native secret storage is unavailable. " + RecoveryHint + " File storage requires explicit consent."
                : "The native secret store failed unexpectedly. Fallback has not been enabled. " + RecoveryHint);
        Publish(new(new("Unavailable (native keystore offline)", false, message), message,
            knownUnavailable && _mode != SecretFallbackMode.Block, _native is not null,
            EncryptedFileSecretStore.FallbackLimitation));
        _warning?.Invoke(message);
    }

    private void SelectFallback(string authorization)
    {
        _active = _fallback;
        var message = $"Encrypted-file fallback authorized by {authorization} for this session. Native secrets are not migrated.";
        Publish(new(_fallback.Info, message, false, false, EncryptedFileSecretStore.FallbackLimitation));
        _warning?.Invoke(message);
    }

    public async Task EnableFallbackAsync(CancellationToken cancellationToken = default)
    {
        Enter();
        try
        {
            await _selectionGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (ReferenceEquals(_active, _fallback))
                {
                    return;
                }
                if (!State.CanEnableFallback)
                {
                    throw new SecretBackendUnavailableException(State.Message);
                }

                if (_settings is not null)
                {
                    // Do not mutate Current before persistence succeeds: a failed save must not grant
                    // consent in memory or incidentally persist it during a later unrelated edit.
                    var copy = JsonSerializer.Deserialize(
                        JsonSerializer.Serialize(_settings.Current, StudioSettingsJsonContext.Default.StudioSettings),
                        StudioSettingsJsonContext.Default.StudioSettings)!;
                    copy.Security = new() { SecretFallback = SecretFallbackMode.Allow };
                    await _settings.SaveAsync(copy, cancellationToken).ConfigureAwait(false);
                }
                cancellationToken.ThrowIfCancellationRequested();
                SelectFallback("explicit user consent");
            }
            finally
            {
                _ = _selectionGate.Release();
            }
        }
        finally
        {
            Leave();
        }
    }

    public async Task RetryNativeAsync(CancellationToken cancellationToken = default)
    {
        Enter();
        try
        {
            await _selectionGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                // Never silently switch away from a session's selected backend.
                if (_active is null)
                {
                    await Task.Run(SelectNativeOrUnavailable, cancellationToken).ConfigureAwait(false);
                }
            }
            finally
            {
                _ = _selectionGate.Release();
            }
        }
        finally
        {
            Leave();
        }
    }

    public async Task SetAsync(string keyRef, string value, CancellationToken cancellationToken = default)
    {
        Enter();
        try
        {
            _ = await RunAsync(async store =>
            {
                await GuardFallbackReferenceAsync(store, keyRef, cancellationToken).ConfigureAwait(false);
                await store.SetAsync(keyRef, value, cancellationToken).ConfigureAwait(false);
                return true;
            }).ConfigureAwait(false);
            await UpdateIndexAsync(keyRef, true, cancellationToken).ConfigureAwait(false);
        }
        finally { Leave(); }
    }

    public async Task<string?> GetAsync(string keyRef, CancellationToken cancellationToken = default)
    {
        Enter();
        try
        {
            return await RunAsync(async store =>
            {
                var value = await store.GetAsync(keyRef, cancellationToken).ConfigureAwait(false);
                if (value is null)
                {
                    await GuardKnownMissingAsync(store, keyRef, cancellationToken).ConfigureAwait(false);
                }
                return value;
            }).ConfigureAwait(false);
        }
        finally { Leave(); }
    }

    public async Task<bool> ExistsAsync(string keyRef, CancellationToken cancellationToken = default)
    {
        Enter();
        try
        {
            return await RunAsync(async store =>
            {
                var exists = await store.ExistsAsync(keyRef, cancellationToken).ConfigureAwait(false);
                if (!exists)
                {
                    await GuardKnownMissingAsync(store, keyRef, cancellationToken).ConfigureAwait(false);
                }
                return exists;
            }).ConfigureAwait(false);
        }
        finally { Leave(); }
    }

    public async Task DeleteAsync(string keyRef, CancellationToken cancellationToken = default)
    {
        Enter();
        try
        {
            _ = await RunAsync(async store =>
            {
                await GuardFallbackReferenceAsync(store, keyRef, cancellationToken).ConfigureAwait(false);
                await store.DeleteAsync(keyRef, cancellationToken).ConfigureAwait(false);
                return true;
            }).ConfigureAwait(false);
            await UpdateIndexAsync(keyRef, false, cancellationToken).ConfigureAwait(false);
        }
        finally { Leave(); }
    }

    public async Task<IReadOnlyList<string>> ListAsync(CancellationToken cancellationToken = default)
    {
        Enter();
        try
        {
            await _indexGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try { return ReadIndex(); }
            finally { _ = _indexGate.Release(); }
        }
        finally { Leave(); }
    }

    private async Task GuardFallbackReferenceAsync(ISecretBackend store, string keyRef, CancellationToken token)
    {
        if (ReferenceEquals(store, _fallback) && !await store.ExistsAsync(keyRef, token).ConfigureAwait(false))
        {
            await GuardKnownMissingAsync(store, keyRef, token).ConfigureAwait(false);
        }
    }

    private async Task GuardKnownMissingAsync(ISecretBackend store, string keyRef, CancellationToken token)
    {
        if (!ReferenceEquals(store, _fallback))
        {
            return;
        }
        await _indexGate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            if (ReadIndex(requireReadable: true).Contains(keyRef))
            {
                throw new SecretBackendUnavailableException(
                    "This stored reference is not available in the selected file backend. Restore native keystore access and restart Studio. The reference has been retained.");
            }
        }
        finally { _ = _indexGate.Release(); }
    }

    private async Task<T> RunAsync<T>(Func<ISecretBackend, Task<T>> operation)
    {
        // One operation, one backend. State changes cannot redirect an admitted operation.
        var store = _active ?? throw new SecretBackendUnavailableException(State.Message);
        try
        {
            var result = await operation(store).ConfigureAwait(false);
            if (ReferenceEquals(store, _native))
            {
                Publish(new(store.Info, "Secrets use the native keystore.", false, false, EncryptedFileSecretStore.FallbackLimitation));
            }
            return result;
        }
        catch (Exception ex) when (ReferenceEquals(store, _native) && IsUnavailable(ex))
        {
            var message = "Native secret storage is unavailable; no fallback was used. " + RecoveryHint;
            Publish(new(store.Info, message, false, false, EncryptedFileSecretStore.FallbackLimitation));
            _warning?.Invoke(message);
            throw new SecretBackendUnavailableException(message);
        }
    }

    private static bool IsUnavailable(Exception ex)
        => ex is SecretBackendUnavailableException or DllNotFoundException or EntryPointNotFoundException;

    private async Task UpdateIndexAsync(string keyRef, bool present, CancellationToken token)
    {
        await _indexGate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            var keys = ReadIndex();
            if (present && !keys.Contains(keyRef))
            {
                keys.Add(keyRef);
                WriteIndex(keys);
            }
            else if (!present && keys.Remove(keyRef))
            {
                WriteIndex(keys);
            }
        }
        finally { _ = _indexGate.Release(); }
    }

    private List<string> ReadIndex(bool requireReadable = false)
    {
        try
        {
            var keys = JsonSerializer.Deserialize<List<string>>(File.ReadAllText(_indexPath));
            if (requireReadable && (keys is null || keys.Any(string.IsNullOrWhiteSpace)))
            {
                throw new JsonException("Invalid secret-reference index.");
            }
            return keys ?? [];
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            return [];
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            if (requireReadable)
            {
                throw new SecretBackendUnavailableException("The secret-reference index cannot be read. File storage cannot determine reference ownership; repair the index before retrying.");
            }
            return [];
        }
    }

    private void WriteIndex(List<string> keys)
    {
        _ = Directory.CreateDirectory(Path.GetDirectoryName(_indexPath)!);
        var tempPath = _indexPath + ".tmp";
        File.WriteAllText(tempPath, JsonSerializer.Serialize(keys));
        File.Move(tempPath, _indexPath, overwrite: true);
    }

    /// <summary>
    ///     Admits one public operation, or rejects it because the store is disposed. The count is
    ///     incremented <em>before</em> the disposed check and the check in <see cref="Dispose" /> reads
    ///     the count after setting the flag, so of two racing threads at least one sees the other: an
    ///     operation is either admitted and drained, or rejected. Both are correct; being admitted and
    ///     then having its backend disposed underneath it is not.
    /// </summary>
    private void Enter()
    {
        _ = Interlocked.Increment(ref _operations);

        if (Volatile.Read(ref _disposed) == 0)
        {
            return;
        }

        Leave();

        // GetType().FullName, matching what ObjectDisposedException.ThrowIf(_, this) produces, so a
        // caller — or a test — can tell a rejection by this facade from one by a backend beneath it.
        throw new ObjectDisposedException(GetType().FullName);
    }

    private void Leave()
    {
        if (Interlocked.Decrement(ref _operations) == 0)
        {
            ReleaseBackendsIfIdle();
        }
    }

    /// <summary>
    ///     Disposes the owned backends once disposal has been requested and no admitted operation is
    ///     still running. Called from both sides of the race — disposal, and the last operation to
    ///     leave — so whichever arrives last does the work; the <see cref="Interlocked" /> guard means
    ///     it happens exactly once.
    ///     <para>
    ///         The two owned fields are disposed rather than <see cref="_active" /> plus
    ///         <see cref="_fallback" />, which would double-dispose whenever the probe failed — in that
    ///         case <c>_active</c> <em>is</em> <c>_fallback</c>. Disposing <see cref="_native" />
    ///         instead also covers the backend whose probe failed, which nothing released before.
    ///     </para>
    ///     Both are pattern-matched rather than cast: <c>MacKeychainSecretStore</c> and
    ///     <c>LinuxLibsecretSecretStore</c> hold no disposable state and do not implement
    ///     <see cref="IDisposable" />, so only the Windows and file backends have anything to release.
    /// </summary>
    private void ReleaseBackendsIfIdle()
    {
        if (Volatile.Read(ref _disposed) == 0
            || Volatile.Read(ref _operations) != 0
            || Interlocked.Exchange(ref _backendsReleased, 1) != 0)
        {
            return;
        }

        (_native as IDisposable)?.Dispose();
        (_fallback as IDisposable)?.Dispose();
    }

    /// <summary>
    ///     Rejects new operations and releases the owned backends. Idempotent, non-throwing, and it
    ///     never blocks: this is a container-owned singleton, and before PRD-005 its throw was one of
    ///     the three that crashed every clean shutdown.
    ///     <para>
    ///         Disposal is deferred while any admitted operation is still running. A public set spans a
    ///         backend write <em>and</em> a later index update, so tearing the backends down on the
    ///         disposed check alone could persist a secret whose index entry never lands, or pull a
    ///         backend out from under a read (PRD-005 re-review, finding 3).
    ///     </para>
    /// </summary>
    public void Dispose()
    {
        // Atomic, so two concurrent disposals cannot both pass the check and race the teardown.
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        ReleaseBackendsIfIdle();
    }
}
