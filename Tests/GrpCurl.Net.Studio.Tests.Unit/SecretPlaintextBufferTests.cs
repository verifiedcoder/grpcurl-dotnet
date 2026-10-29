using GrpCurl.Net.Studio.Services.Secrets;
using GrpCurl.Net.Studio.ViewModels.Services;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace GrpCurl.Net.Studio.Tests.Unit;

public sealed class SecretPlaintextBufferTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "grpcn-prd039-plaintext-" + Guid.NewGuid().ToString("N"));
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose()
    {
        if (Directory.Exists(_directory)) { Directory.Delete(_directory, true); }
    }

    [Theory]
    [InlineData("fallback", "secret")]
    [InlineData("fallback", "密碼 🔐 café")]
    [InlineData("fallback", "")]
    [InlineData("dpapi", "secret")]
    [InlineData("dpapi", "密碼 🔐 café")]
    [InlineData("dpapi", "")]
    public async Task Actual_plaintext_buffers_are_cleared_before_persistence_and_after_conversion(string backend, string value)
    {
        SkipUnavailable(backend);
        var buffers = new List<byte[]>();
        var stages = new List<PlaintextBufferStage>();
        var persistedAfterClear = false;
        var files = new SecureSecretFile((stage, _) =>
        {
            if (stage == SecretFileStage.Created && buffers.Count > 0)
            {
                foreach (var buffer in buffers) { buffer.ShouldAllBe(b => b == 0); }
                persistedAfterClear = true;
            }
        });
        var store = Create(backend, files, (stage, buffer) =>
        {
            buffers.Add(buffer); // retain the real array, not a copy
            stages.Add(stage);
            if (stage == PlaintextBufferStage.DecryptAllocated) { buffer.ShouldAllBe(b => b == 0); }
            else
            {
                buffer.ShouldBe(Encoding.UTF8.GetBytes(value));
                if (value.Length > 0) { buffer.ShouldContain(b => b != 0); }
            }
        });
        using var lifetime = (IDisposable)store;
        await store.SetAsync("key", value, Ct);
        stages.ShouldBe([PlaintextBufferStage.EncryptInput]);
        persistedAfterClear.ShouldBeTrue();
        (await store.GetAsync("key", Ct)).ShouldBe(value);
        stages.ShouldContain(PlaintextBufferStage.DecryptOutput);
        if (backend == "fallback")
        {
            stages.ShouldContain(PlaintextBufferStage.DecryptAllocated);
            buffers[1].ShouldBeSameAs(buffers[2]);
        }
        foreach (var buffer in buffers) { buffer.ShouldAllBe(b => b == 0); }
        var reopened = Create(backend);
        using var reopenedLifetime = (IDisposable)reopened;
        (await reopened.GetAsync("key", Ct)).ShouldBe(value);
    }

    [Theory]
    [InlineData("fallback", PlaintextBufferStage.EncryptInput)]
    [InlineData("fallback", PlaintextBufferStage.DecryptAllocated)]
    [InlineData("fallback", PlaintextBufferStage.DecryptOutput)]
    [InlineData("dpapi", PlaintextBufferStage.EncryptInput)]
    [InlineData("dpapi", PlaintextBufferStage.DecryptOutput)]
    internal async Task Failure_inside_owned_lifetime_clears_actual_buffer_and_keeps_persisted_value(
        string backend, PlaintextBufferStage failAt)
    {
        SkipUnavailable(backend);
        var fail = false;
        byte[]? observed = null;
        var store = Create(backend, checkpoint: (stage, buffer) =>
        {
            if (!fail || stage != failAt) { return; }
            observed = buffer;
            if (stage == PlaintextBufferStage.DecryptAllocated)
            {
                // A fresh array starts zeroed. Seed it so this assertion detects missing cleanup.
                buffer.AsSpan().Fill(0xa5);
            }
            buffer.ShouldContain(b => b != 0);
            throw new InvalidOperationException("Injected before crypto/conversion.");
        });
        using var lifetime = (IDisposable)store;
        await store.SetAsync("key", "original", Ct);
        var path = DataPath(backend);
        var before = File.ReadAllBytes(path);
        fail = true;
        _ = await Should.ThrowAsync<InvalidOperationException>(() => failAt == PlaintextBufferStage.EncryptInput
            ? store.SetAsync("key", "replacement", Ct) : store.GetAsync("key", Ct));
        observed.ShouldNotBeNull().ShouldAllBe(b => b == 0);
        File.ReadAllBytes(path).ShouldBe(before);
        fail = false;
        (await store.GetAsync("key", Ct)).ShouldBe("original");
    }

    [Theory]
    [InlineData("fallback")]
    [InlineData("dpapi")]
    public async Task Cryptographic_failure_clears_encryption_input_without_persisting(string backend)
    {
        SkipUnavailable(backend);
        byte[]? observed = null;
        var store = Create(backend, checkpoint: (_, buffer) =>
        {
            observed = buffer;
            buffer.ShouldContain(b => b != 0);
            throw new CryptographicException("Injected crypto failure.");
        });
        using var lifetime = (IDisposable)store;
        _ = await Should.ThrowAsync<CryptographicException>(() => store.SetAsync("key", "secret", Ct));
        observed.ShouldNotBeNull().ShouldAllBe(b => b == 0);
        File.Exists(DataPath(backend)).ShouldBeFalse();
    }

    [Fact]
    public async Task Authentication_failure_clears_fallback_decryption_destination_and_keeps_cached_key_usable()
    {
        byte[]? destination = null;
        var outputReached = false;
        using var store = new EncryptedFileSecretStore(_directory, plaintextCheckpoint: (stage, buffer) =>
        {
            if (stage == PlaintextBufferStage.DecryptAllocated)
            {
                destination = buffer;
                buffer.AsSpan().Fill(0xa5);
            }
            if (stage == PlaintextBufferStage.DecryptOutput) { outputReached = true; }
        });
        await store.SetAsync("key", "secret", Ct);
        var key = store.KeyForTests.ShouldNotBeNull();
        var keyBefore = key.ToArray();
        var path = DataPath("fallback");
        var original = File.ReadAllText(path);
        var values = JsonSerializer.Deserialize<Dictionary<string, string>>(original)!;
        var parts = values["key"].Split('.');
        var tag = Convert.FromBase64String(parts[1]);
        tag[0] ^= 1;
        parts[1] = Convert.ToBase64String(tag);
        values["key"] = string.Join('.', parts);
        File.WriteAllText(path, JsonSerializer.Serialize(values));
        (await store.GetAsync("key", Ct)).ShouldBeNull();
        destination.ShouldNotBeNull().ShouldAllBe(b => b == 0);
        outputReached.ShouldBeFalse();
        store.KeyForTests.ShouldBeSameAs(key);
        key.ShouldBe(keyBefore);
        File.WriteAllText(path, original);
        (await store.GetAsync("key", Ct)).ShouldBe("secret");
        await store.SetAsync("second", "other", Ct);
        (await store.GetAsync("second", Ct)).ShouldBe("other");
        key.ShouldBe(keyBefore);
        store.Dispose();
        key.ShouldAllBe(b => b == 0);
    }

    [Theory]
    [InlineData("fallback")]
    [InlineData("dpapi")]
    public async Task Persistence_failure_happens_after_plaintext_is_already_cleared(string backend)
    {
        SkipUnavailable(backend);
        byte[]? observed = null;
        var files = new SecureSecretFile((stage, _) =>
        {
            if (observed is not null && stage == SecretFileStage.PartialWrite)
            {
                observed.ShouldAllBe(b => b == 0);
                throw new IOException("Injected persistence failure.");
            }
        });
        var store = Create(backend, files, (_, buffer) => observed = buffer);
        using var lifetime = (IDisposable)store;
        _ = await Should.ThrowAsync<SecretStorageException>(() => store.SetAsync("key", "secret", Ct));
        observed.ShouldNotBeNull().ShouldAllBe(b => b == 0);
        File.Exists(DataPath(backend)).ShouldBeFalse();
    }

    private static void SkipUnavailable(string backend)
        => Assert.SkipWhen(backend == "dpapi" && !OperatingSystem.IsWindows(), "Windows DPAPI only.");

    private string DataPath(string backend) => Path.Combine(_directory,
        backend == "dpapi" ? "secrets.dpapi.json" : "secrets.json");

    private ISecretBackend Create(string backend, SecureSecretFile? files = null,
        Action<PlaintextBufferStage, byte[]>? checkpoint = null)
        => backend == "dpapi" && OperatingSystem.IsWindows()
            ? new WindowsDpapiSecretStore(_directory, files, checkpoint)
            : new EncryptedFileSecretStore(_directory, files, plaintextCheckpoint: checkpoint);
}
