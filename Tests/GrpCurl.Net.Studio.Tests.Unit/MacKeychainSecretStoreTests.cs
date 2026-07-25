using GrpCurl.Net.Studio.Services.Secrets;
using System.Diagnostics;

namespace GrpCurl.Net.Studio.Tests.Unit;

/// <summary>
///     PRD-001. Two layers of coverage:
///     <list type="bullet">
///         <item>Backend behavior through an injected fake <see cref="IKeychainNative" /> — round-trip and
///             every OSStatus error/locked/not-found path — runs deterministically on every CI OS.</item>
///         <item>The real macOS Keychain facts (CRUD round-trip, cancellation, and the argv-canary that
///             proves no secret ever reaches a process command line) run only on macOS, and skip cleanly
///             when the CI keychain is locked/unavailable.</item>
///     </list>
/// </summary>
public sealed class MacKeychainSecretStoreTests
{
    // ---- Backend-behavior tests via a fake native adapter (all OSes) -------------------------------

    [Fact]
    public async Task Backend_round_trips_through_the_native_adapter()
    {
        var store = new MacKeychainSecretStore(new FakeKeychainNative());
        var ct = TestContext.Current.CancellationToken;

        (await store.ExistsAsync("k", ct)).ShouldBeFalse();

        await store.SetAsync("k", "s3cr3t-v1", ct);
        (await store.GetAsync("k", ct)).ShouldBe("s3cr3t-v1");
        (await store.ExistsAsync("k", ct)).ShouldBeTrue();

        await store.SetAsync("k", "s3cr3t-v2", ct); // update-on-duplicate
        (await store.GetAsync("k", ct)).ShouldBe("s3cr3t-v2");

        await store.DeleteAsync("k", ct);
        (await store.GetAsync("k", ct)).ShouldBeNull();
    }

    [Fact]
    public async Task Get_returns_null_when_the_item_is_not_found()
    {
        var store = new MacKeychainSecretStore(new FakeKeychainNative());

        (await store.GetAsync("absent", TestContext.Current.CancellationToken)).ShouldBeNull();
    }

    [Fact]
    public async Task Delete_is_idempotent_when_the_item_is_absent()
    {
        var store = new MacKeychainSecretStore(new FakeKeychainNative());

        // Must not throw even though nothing is stored.
        await store.DeleteAsync("absent", TestContext.Current.CancellationToken);
    }

    [Theory]
    [InlineData(KeychainStatusMapping.ErrSecInteractionNotAllowed)] // keychain locked
    [InlineData(KeychainStatusMapping.ErrSecAuthFailed)]            // auth denied
    [InlineData(-1)]                                                // unexpected
    public async Task Set_surfaces_native_failures_as_exceptions(int status)
    {
        var store = new MacKeychainSecretStore(new FakeKeychainNative { ForcedStatus = status });

        var ex = await Should.ThrowAsync<InvalidOperationException>(
            () => store.SetAsync("k", "s3cr3t", TestContext.Current.CancellationToken));
        ex.Message.ShouldContain($"OSStatus {status}");
    }

    [Theory]
    [InlineData(KeychainStatusMapping.ErrSecInteractionNotAllowed)]
    [InlineData(KeychainStatusMapping.ErrSecAuthFailed)]
    [InlineData(-1)]
    public async Task Get_surfaces_native_failures_as_exceptions(int status)
    {
        var store = new MacKeychainSecretStore(new FakeKeychainNative { ForcedStatus = status });

        _ = await Should.ThrowAsync<InvalidOperationException>(
            () => store.GetAsync("k", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Delete_surfaces_an_unexpected_native_failure_as_an_exception()
    {
        var store = new MacKeychainSecretStore(new FakeKeychainNative { ForcedStatus = -1 });

        _ = await Should.ThrowAsync<InvalidOperationException>(
            () => store.DeleteAsync("k", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Pre_cancelled_set_throws_and_never_touches_the_native_adapter()
    {
        var native = new FakeKeychainNative();
        var store = new MacKeychainSecretStore(native);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        _ = await Should.ThrowAsync<OperationCanceledException>(() => store.SetAsync("k", "s3cr3t", cts.Token));

        native.UpsertCalls.ShouldBe(0); // nothing was written
    }

    // Message hygiene: no OSStatus mapping may echo a secret or keyRef into the exception text.
    [Theory]
    [InlineData(KeychainStatusMapping.ErrSecInteractionNotAllowed)]
    [InlineData(KeychainStatusMapping.ErrSecAuthFailed)]
    [InlineData(KeychainStatusMapping.ErrSecDuplicateItem)]
    [InlineData(-1)]
    public void Exception_messages_carry_only_the_operation_and_status_never_a_secret(int status)
    {
        const string secret = "sh0uld-never-appear";
        const string keyRef = "studio/v1/tls/should-never-appear-either";

        var ex = KeychainStatusMapping.ToException(status, "add/update");

        _ = ex.ShouldBeOfType<InvalidOperationException>();
        ex.Message.ShouldContain("add/update");
        ex.Message.ShouldMatch(@"OSStatus -?\d+");
        ex.Message.ShouldNotContain(secret);
        ex.Message.ShouldNotContain(keyRef);
    }

    // ---- Real macOS Keychain facts (macOS only) ---------------------------------------------------

    [Fact]
    public async Task Round_trips_create_update_read_delete_and_is_idempotent_on_redelete()
    {
        Assert.SkipWhen(!OperatingSystem.IsMacOS(), "macOS Keychain only.");

        if (!OperatingSystem.IsMacOS())
        {
            return;
        }

        var store = new MacKeychainSecretStore();
        var ct = TestContext.Current.CancellationToken;
        Assert.SkipUnless(await KeychainUsableAsync(store, ct), "macOS Keychain is locked/unavailable on this host.");

        var key = "grpcn-test-" + Guid.NewGuid().ToString("N");

        try
        {
            (await store.ExistsAsync(key, ct)).ShouldBeFalse();

            await store.SetAsync(key, "s3cr3t-v1", ct);
            (await store.GetAsync(key, ct)).ShouldBe("s3cr3t-v1");
            (await store.ExistsAsync(key, ct)).ShouldBeTrue();

            // Update: SetAsync on an existing keyRef must overwrite, not fail on duplicate.
            await store.SetAsync(key, "s3cr3t-v2", ct);
            (await store.GetAsync(key, ct)).ShouldBe("s3cr3t-v2");

            await store.DeleteAsync(key, ct);
            (await store.GetAsync(key, ct)).ShouldBeNull();

            // Idempotent: deleting an already-absent item must not throw.
            await store.DeleteAsync(key, ct);
        }
        finally
        {
            await store.DeleteAsync(key, ct);
        }
    }

    [Fact]
    public async Task Cancellation_before_the_call_throws_and_creates_nothing()
    {
        Assert.SkipWhen(!OperatingSystem.IsMacOS(), "macOS Keychain only.");

        if (!OperatingSystem.IsMacOS())
        {
            return;
        }

        var store = new MacKeychainSecretStore();
        var ct = TestContext.Current.CancellationToken;
        Assert.SkipUnless(await KeychainUsableAsync(store, ct), "macOS Keychain is locked/unavailable on this host.");

        var key = "grpcn-test-" + Guid.NewGuid().ToString("N");
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        try
        {
            _ = await Should.ThrowAsync<OperationCanceledException>(() => store.SetAsync(key, "s3cr3t", cts.Token));
            (await store.ExistsAsync(key, ct)).ShouldBeFalse();
        }
        finally
        {
            await store.DeleteAsync(key, ct);
        }
    }

    [Fact]
    public async Task Setting_a_secret_never_appears_in_any_process_command_line()
    {
        Assert.SkipWhen(!OperatingSystem.IsMacOS(), "macOS Keychain only.");

        if (!OperatingSystem.IsMacOS())
        {
            return;
        }

        var store = new MacKeychainSecretStore();
        var ct = TestContext.Current.CancellationToken;
        Assert.SkipUnless(await KeychainUsableAsync(store, ct), "macOS Keychain is locked/unavailable on this host.");

        var key = "grpcn-test-" + Guid.NewGuid().ToString("N");
        var canary = "CANARY-" + Guid.NewGuid().ToString("N");

        var sightings = new List<string>();
        var firstSample = new TaskCompletionSource();
        var sampleCount = 0;
        using var stop = new CancellationTokenSource();

        var sampler = Task.Run(async () =>
        {
            try
            {
                while (!stop.IsCancellationRequested)
                {
                    foreach (var line in await SampleProcessCommandLinesAsync(ct))
                    {
                        if (line.Contains(canary, StringComparison.Ordinal) || line.Contains("/usr/bin/security", StringComparison.Ordinal))
                        {
                            lock (sightings)
                            {
                                sightings.Add(line);
                            }
                        }
                    }

                    _ = Interlocked.Increment(ref sampleCount);
                    _ = firstSample.TrySetResult();
                    await Task.Delay(2, stop.Token);
                }
            }
            catch (OperationCanceledException)
            {
                // Normal shutdown of the sampler.
            }
        }, ct);

        try
        {
            // Barrier: don't start writing until the sampler has proven it can capture command lines,
            // otherwise the assertion below could pass without ever inspecting a single sample.
            await firstSample.Task.WaitAsync(TimeSpan.FromSeconds(10), ct);
            var samplesBeforeWrites = Volatile.Read(ref sampleCount);

            for (var i = 0; i < 50; i++)
            {
                await store.SetAsync(key, canary, ct);
            }

            // Prove sampling actually continued *during* the writes, not just before them.
            Volatile.Read(ref sampleCount).ShouldBeGreaterThan(samplesBeforeWrites);
        }
        finally
        {
            await stop.CancelAsync();
            await sampler;
            await store.DeleteAsync(key, ct);
        }

        // The Keychain backend no longer shells out at all — neither the canary value nor a `security`
        // child process should ever have been observable via process listing.
        sightings.ShouldBeEmpty();
    }

    private static async Task<bool> KeychainUsableAsync(MacKeychainSecretStore store, CancellationToken cancellationToken)
    {
        var probe = "grpcn-probe-" + Guid.NewGuid().ToString("N");
        try
        {
            await store.SetAsync(probe, "probe", cancellationToken);
            await store.DeleteAsync(probe, cancellationToken);
            return true;
        }
        catch (InvalidOperationException)
        {
            return false; // locked / access denied on this host
        }
        catch (PlatformNotSupportedException)
        {
            return false;
        }
    }

    private static async Task<string[]> SampleProcessCommandLinesAsync(CancellationToken cancellationToken)
    {
        var psi = new ProcessStartInfo("ps")
        {
            RedirectStandardOutput = true,
            UseShellExecute = false
        };
        psi.ArgumentList.Add("-axo");
        psi.ArgumentList.Add("command");

        using var process = Process.Start(psi) ?? throw new InvalidOperationException("Could not start ps.");
        var output = await process.StandardOutput.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);

        return output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
    }

    private sealed class FakeKeychainNative : IKeychainNative
    {
        private readonly Dictionary<string, byte[]> _store = [];

        /// <summary>When set to a non-success status, every operation returns it instead of touching
        ///     the in-memory store — used to drive the error/locked/not-found paths.</summary>
        public int? ForcedStatus { get; init; }

        public int UpsertCalls { get; private set; }

        public int Upsert(string service, string account, byte[] secretUtf8)
        {
            UpsertCalls++;
            if (ForcedStatus is { } forced)
            {
                return forced;
            }

            _store[account] = (byte[])secretUtf8.Clone();
            return KeychainStatusMapping.ErrSecSuccess;
        }

        public int TryFind(string service, string account, out byte[]? secretUtf8)
        {
            if (ForcedStatus is { } forced)
            {
                secretUtf8 = null;
                return forced;
            }

            if (_store.TryGetValue(account, out var stored))
            {
                secretUtf8 = (byte[])stored.Clone();
                return KeychainStatusMapping.ErrSecSuccess;
            }

            secretUtf8 = null;
            return KeychainStatusMapping.ErrSecItemNotFound;
        }

        public int Delete(string service, string account)
        {
            if (ForcedStatus is { } forced)
            {
                return forced;
            }

            return _store.Remove(account) ? KeychainStatusMapping.ErrSecSuccess : KeychainStatusMapping.ErrSecItemNotFound;
        }
    }
}
