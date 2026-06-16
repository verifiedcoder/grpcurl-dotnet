using GrpCurl.Net.Studio.Services.Secrets;

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
    public async Task Facade_round_trips_via_native_or_fallback()
    {
        var store = new SecretStore(_dir);
        var key = Guid.NewGuid().ToString("N");

        try
        {
            await store.SetAsync(key, "facade-secret", TestContext.Current.CancellationToken);
            (await store.GetAsync(key, TestContext.Current.CancellationToken)).ShouldBe("facade-secret");
        }
        finally
        {
            await store.DeleteAsync(key, TestContext.Current.CancellationToken);
        }
    }
}
