using GrpCurl.Net.Studio.ViewModels.Services;
using System.ComponentModel;
using System.Text.Json;

namespace GrpCurl.Net.Studio.Services.Secrets;

/// <summary>
///     Default <see cref="ISecretStore" /> (SEC-020..025): selects a backend once at startup — the per-OS
///     native keystore (Windows DPAPI, macOS Keychain, Linux Secret Service) if a probe shows it is
///     available, otherwise the encrypted-file fallback. The selection is logged (backend name only) and
///     surfaced via <see cref="Info" /> for Settings → Security. If the native backend later fails mid-session
///     it transparently switches to the fallback (defence in depth); migration of already-stored secrets
///     between backends is out of scope for v1 (SEC-025) — a missed keyref means the user re-enters the value.
/// </summary>
internal sealed class SecretStore : ISecretStore, IDisposable
{
    private const string ProbeKeyRef = "studio/v1/app/probe";
    private const string IndexFileName = "secret-keyrefs.json";

    private readonly ISecretBackend _fallback;
    private readonly string _indexPath;

    /// <summary>
    ///     Serialises index reads and writes. Never disposed, for the reason given on
    ///     <c>EncryptedFileSecretStore._gate</c>: it is only ever awaited, so there is nothing to
    ///     release, and disposing it is what would make an admitted waiter undefined.
    /// </summary>
    private readonly SemaphoreSlim _indexGate = new(1, 1);

    /// <summary>
    ///     The native backend this store constructed, or <see langword="null" /> on a platform without
    ///     one. Held as a field rather than read off <see cref="_active" /> at disposal time because
    ///     <see cref="DemoteToFallback" /> can reassign <c>_active</c> mid-session, and because a
    ///     backend whose probe failed is never assigned to <c>_active</c> at all — before PRD-005 that
    ///     one was simply dropped, leaking the Windows backend's semaphore on every failed probe.
    /// </summary>
    private readonly ISecretBackend? _native;

    private volatile ISecretBackend _active;
    private volatile bool _activeIsNative;

    private int _disposed;

    /// <summary>
    ///     How many public operations have been admitted and not yet finished. Disposal is only allowed
    ///     to destroy the backends when this reaches zero, so a set that has written to the keychain but
    ///     not yet updated the index — or any read still in the backend — completes against a live
    ///     backend rather than a torn-down one (PRD-005 re-review, finding 3).
    /// </summary>
    private int _operations;

    private int _backendsReleased;

    public SecretStore(string directory, Action<string>? log = null)
    {
        _fallback = new EncryptedFileSecretStore(directory);
        _indexPath = Path.Combine(directory, IndexFileName);

        _native = OperatingSystem.IsWindows() ? new WindowsDpapiSecretStore(directory)
            : OperatingSystem.IsMacOS() ? (ISecretBackend?)new MacKeychainSecretStore()
            : OperatingSystem.IsLinux() ? new LinuxLibsecretSecretStore()
            : null;

        // SEC-025: probe the native backend once at startup; commit to native or fallback now.
        if (_native is not null && Probe(_native))
        {
            _active = _native;
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
        Enter();

        try
        {
            await RunAsync(store => store.SetAsync(keyRef, value, cancellationToken)).ConfigureAwait(false);
            await UpdateIndexAsync(keyRef, present: true, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            Leave();
        }
    }

    public async Task<string?> GetAsync(string keyRef, CancellationToken cancellationToken = default)
    {
        Enter();

        try
        {
            return await RunAsync(store => store.GetAsync(keyRef, cancellationToken)).ConfigureAwait(false);
        }
        finally
        {
            Leave();
        }
    }

    public async Task DeleteAsync(string keyRef, CancellationToken cancellationToken = default)
    {
        Enter();

        try
        {
            await RunAsync(store => store.DeleteAsync(keyRef, cancellationToken)).ConfigureAwait(false);
            await UpdateIndexAsync(keyRef, present: false, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            Leave();
        }
    }

    public async Task<bool> ExistsAsync(string keyRef, CancellationToken cancellationToken = default)
    {
        Enter();

        try
        {
            return await RunAsync(store => store.ExistsAsync(keyRef, cancellationToken)).ConfigureAwait(false);
        }
        finally
        {
            Leave();
        }
    }

    // SEC-027: enumeration is served from the router's keyref index, kept in sync on every set/delete — the
    // native keychains cannot be portably listed, so the index is the single source of truth across backends.
    public async Task<IReadOnlyList<string>> ListAsync(CancellationToken cancellationToken = default)
    {
        Enter();

        try
        {
            await _indexGate.WaitAsync(cancellationToken).ConfigureAwait(false);

            try
            {
                return ReadIndex();
            }
            finally
            {
                _ = _indexGate.Release();
            }
        }
        finally
        {
            Leave();
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
            _ = _indexGate.Release();
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
        _ = Directory.CreateDirectory(Path.GetDirectoryName(_indexPath)!);
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
            _ = native.ExistsAsync(ProbeKeyRef).GetAwaiter().GetResult();
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
