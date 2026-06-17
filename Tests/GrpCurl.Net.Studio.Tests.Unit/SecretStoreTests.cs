using GrpCurl.Net.Studio.Services.Secrets;
using GrpCurl.Net.Studio.ViewModels.Services;

namespace GrpCurl.Net.Studio.Tests.Unit;

/// <summary>
///     Secret-store tests. The encrypted-file fallback is exercised directly (CI-safe everywhere); the
///     <see cref="SecretStore" /> facade round-trip runs through the per-OS native backend where it's
///     available and falls back transparently otherwise — so it passes on a headless box too.
/// </summary>
public sealed class SecretStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "grpcn-secret-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch (IOException)
        {
            // Best-effort temp cleanup.
        }
    }

    [Fact]
    public async Task Encrypted_file_store_round_trips_set_get_delete()
    {
        var store = new EncryptedFileSecretStore(_dir);
        var key = Guid.NewGuid().ToString("N");

        await store.SetAsync(key, "s3cr3t", TestContext.Current.CancellationToken);
        (await store.GetAsync(key, TestContext.Current.CancellationToken)).ShouldBe("s3cr3t");

        await store.DeleteAsync(key, TestContext.Current.CancellationToken);
        (await store.GetAsync(key, TestContext.Current.CancellationToken)).ShouldBeNull();
    }

    [Fact]
    public async Task Encrypted_file_store_overwrites_and_isolates_keys()
    {
        var store = new EncryptedFileSecretStore(_dir);

        await store.SetAsync("a", "1", TestContext.Current.CancellationToken);
        await store.SetAsync("b", "2", TestContext.Current.CancellationToken);
        await store.SetAsync("a", "11", TestContext.Current.CancellationToken);

        (await store.GetAsync("a", TestContext.Current.CancellationToken)).ShouldBe("11");
        (await store.GetAsync("b", TestContext.Current.CancellationToken)).ShouldBe("2");
        (await store.GetAsync("missing", TestContext.Current.CancellationToken)).ShouldBeNull();
    }

    [Fact]
    public async Task Encrypted_file_store_never_keeps_plaintext_on_disk()
    {
        var store = new EncryptedFileSecretStore(_dir);
        await store.SetAsync("k", "PLAINTEXT-MARKER", TestContext.Current.CancellationToken);

        var raw = await File.ReadAllTextAsync(Path.Combine(_dir, "secrets.json"), TestContext.Current.CancellationToken);
        raw.ShouldNotContain("PLAINTEXT-MARKER");
    }

    [Fact]
    public async Task Encrypted_file_store_reports_existence()
    {
        var store = new EncryptedFileSecretStore(_dir);
        var ct = TestContext.Current.CancellationToken;

        (await store.ExistsAsync("k", ct)).ShouldBeFalse();
        await store.SetAsync("k", "v", ct);
        (await store.ExistsAsync("k", ct)).ShouldBeTrue();
        await store.DeleteAsync("k", ct);
        (await store.ExistsAsync("k", ct)).ShouldBeFalse();
    }

    [Fact]
    public void Encrypted_file_store_info_is_the_fallback_with_an_honest_limitation()
    {
        var info = new EncryptedFileSecretStore(_dir).Info;

        info.BackendName.ShouldBe("Encrypted file (fallback)");
        info.IsOsKeychain.ShouldBeFalse(); // SEC-024: false only for the fallback
        info.LimitationNote.ShouldNotBeNull();
        info.LimitationNote.ShouldContain("Secret Service"); // recommends a keyring provider
    }

    [Fact]
    public async Task Encrypted_file_store_derives_a_stable_key_across_instances()
    {
        var key = Guid.NewGuid().ToString("N");
        var ct = TestContext.Current.CancellationToken;

        await new EncryptedFileSecretStore(_dir).SetAsync(key, "derived-secret", ct);

        // SEC-023: the key is HKDF-derived from machine + user + salt, not cached — a fresh instance
        // over the same directory must derive the same key and decrypt successfully.
        (await new EncryptedFileSecretStore(_dir).GetAsync(key, ct)).ShouldBe("derived-secret");
    }

    [Fact]
    public async Task Encrypted_file_store_persists_a_salt_not_a_raw_key()
    {
        await new EncryptedFileSecretStore(_dir).SetAsync("k", "v", TestContext.Current.CancellationToken);

        File.Exists(Path.Combine(_dir, "secrets.salt")).ShouldBeTrue(); // SEC-023: only the salt is at rest
        File.Exists(Path.Combine(_dir, "secrets.key")).ShouldBeFalse(); // never a raw key

        if (!OperatingSystem.IsWindows())
        {
            var mode = File.GetUnixFileMode(Path.Combine(_dir, "secrets.salt"));
            mode.ShouldBe(UnixFileMode.UserRead | UnixFileMode.UserWrite); // 0600
        }
    }

    [Fact]
    public async Task Facade_round_trips_via_native_or_fallback()
    {
        var store = new SecretStore(_dir);
        var key = Guid.NewGuid().ToString("N");

        try
        {
            await store.SetAsync(key, "facade-secret", TestContext.Current.CancellationToken);
            (await store.GetAsync(key, TestContext.Current.CancellationToken)).ShouldBe("facade-secret");
            (await store.ExistsAsync(key, TestContext.Current.CancellationToken)).ShouldBeTrue();
        }
        finally
        {
            await store.DeleteAsync(key, TestContext.Current.CancellationToken);
        }
    }

    [Fact]
    public void Facade_selects_and_logs_a_backend_at_startup()
    {
        string? logged = null;
        var store = new SecretStore(_dir, log: m => logged = m);

        // SEC-024/025: a backend is chosen at startup, surfaced via Info, and logged (name only).
        store.Info.BackendName.ShouldNotBeNullOrWhiteSpace();
        logged.ShouldNotBeNull();
        logged.ShouldContain(store.Info.BackendName);
    }

    [Fact]
    public void Facade_info_is_a_secret_store_info()
        => new SecretStore(_dir).Info.ShouldBeOfType<SecretStoreInfo>();
}
