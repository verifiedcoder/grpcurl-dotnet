using GrpCurl.Net.Studio.Services.Secrets;
using System.Diagnostics;

namespace GrpCurl.Net.Studio.Tests.Unit;

/// <summary>
///     PRD-001: proves the macOS Keychain backend never puts a secret value on argv (the real Keychain
///     CRUD/cancellation facts, macOS-only), and that its OSStatus→exception mapping
///     (<see cref="KeychainStatusMapping" />) never discloses a secret in a log/exception message (a
///     pure, platform-independent check that runs on every CI OS).
/// </summary>
public sealed class MacKeychainSecretStoreTests
{
    [Fact]
    public async Task Round_trips_create_update_read_delete_and_is_idempotent_on_redelete()
    {
        Assert.SkipWhen(!OperatingSystem.IsMacOS(), "macOS Keychain only.");

        // The Assert.SkipWhen above is a runtime-only gate (it throws a SkipException the MTP runner
        // renders as "skipped"); the platform-compatibility analyzer needs a structural guard too, since
        // MacKeychainSecretStore is [SupportedOSPlatform("macos")].
        if (!OperatingSystem.IsMacOS())
        {
            return;
        }

        var store = new MacKeychainSecretStore();
        var key = "grpcn-test-" + Guid.NewGuid().ToString("N");
        var ct = TestContext.Current.CancellationToken;

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
        var key = "grpcn-test-" + Guid.NewGuid().ToString("N");
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        try
        {
            _ = await Should.ThrowAsync<OperationCanceledException>(() => store.SetAsync(key, "s3cr3t", cts.Token));
            (await store.ExistsAsync(key, TestContext.Current.CancellationToken)).ShouldBeFalse();
        }
        finally
        {
            await store.DeleteAsync(key, TestContext.Current.CancellationToken);
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
        var key = "grpcn-test-" + Guid.NewGuid().ToString("N");
        var canary = "CANARY-" + Guid.NewGuid().ToString("N");
        var ct = TestContext.Current.CancellationToken;

        var sightings = new List<string>();
        using var stop = new CancellationTokenSource();

        var sampler = Task.Run(async () =>
        {
            while (!stop.IsCancellationRequested)
            {
                foreach (var line in await SampleProcessCommandLinesAsync(ct))
                {
                    if (line.Contains(canary, StringComparison.Ordinal) || line.Contains("/usr/bin/security", StringComparison.Ordinal))
                    {
                        sightings.Add(line);
                    }
                }

                await Task.Delay(5, ct);
            }
        }, ct);

        try
        {
            for (var i = 0; i < 50; i++)
            {
                await store.SetAsync(key, canary, ct);
            }
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

    // Pure OSStatus classification, deliberately NOT [SupportedOSPlatform]-gated (see
    // KeychainStatusMapping's doc comment) — this runs (and must pass) on all three CI OSes, unlike the
    // facts above.
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
}
