using GrpCurl.Net.Studio.Services.Secrets;
using GrpCurl.Net.Studio.ViewModels.Connections;
using GrpCurl.Net.Studio.ViewModels.Models;
using GrpCurl.Net.Studio.ViewModels.Services;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace GrpCurl.Net.Studio.Tests.Unit;

public sealed class SecretFileStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "grpcn-file-store-" + Guid.NewGuid().ToString("N"));
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose()
    {
        if (Directory.Exists(_directory)) { Directory.Delete(_directory, true); }
    }

    [Theory]
    [InlineData("fallback")]
    [InlineData("dpapi")]
    [InlineData("index")]
    public async Task Each_store_retains_its_previous_file_on_failure_and_reopens_after_success(string backend)
    {
        Assert.SkipWhen(backend == "dpapi" && !OperatingSystem.IsWindows(), "Windows DPAPI only.");
        var fail = false;
        var files = new SecureSecretFile((stage, _) =>
        {
            if (fail && stage == SecretFileStage.PartialWrite) { throw new IOException("raw-canary"); }
        });
        var fallback = new EncryptedFileSecretStore(_directory, backend == "fallback" ? files : null);
        ISecretBackend data = backend == "dpapi" && OperatingSystem.IsWindows()
            ? new WindowsDpapiSecretStore(_directory, files) : fallback;
        using var facade = new SecretStore(_directory, data, fallback, files: backend == "index" ? files : null);
        await facade.SetAsync("first", "old", Ct);
        var path = Path.Combine(_directory, backend == "index" ? "secret-keyrefs.json" : backend == "dpapi" ? "secrets.dpapi.json" : "secrets.json");
        var old = File.ReadAllText(path);
        fail = true;
        var error = await Should.ThrowAsync<SecretStorageException>(() => facade.SetAsync("second", "new", Ct));
        File.ReadAllText(path).ShouldBe(old);
        (await facade.GetAsync("first", Ct)).ShouldBe("old");
        if (backend == "index")
        {
            error.Message.ShouldContain("secret change completed");
            (await facade.GetAsync("second", Ct)).ShouldBe("new");
            (await facade.ListAsync(Ct)).ShouldBe(["first"]);
        }
        fail = false;
        await facade.SetAsync("second", "new", Ct);
        SecureSecretFileTests.AssertPrivate(path, false);
        if (backend == "dpapi" && OperatingSystem.IsWindows())
        {
            using var reopened = new WindowsDpapiSecretStore(_directory);
            (await reopened.GetAsync("second", Ct)).ShouldBe("new");
        }
        else
        {
            using var reopened = new EncryptedFileSecretStore(_directory);
            (await reopened.GetAsync("second", Ct)).ShouldBe("new");
        }
        (await facade.ListAsync(Ct)).ShouldBe(["first", "second"]);
    }

    [Theory]
    [InlineData("secrets.salt")]
    [InlineData("machine.id")]
    public async Task Racing_initializers_use_the_winners_inputs_and_can_decrypt_the_committed_ciphertext(string input)
    {
        using var ready = new Barrier(2);
        // Pre-initialize the other input, so both writers stop at exactly the input under test.
        _ = Directory.CreateDirectory(_directory);
        File.WriteAllText(Path.Combine(_directory, input == "secrets.salt" ? "machine.id" : "secrets.salt"),
            input == "secrets.salt" ? Guid.NewGuid().ToString("N") : Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)));
        var files = new SecureSecretFile((stage, _) =>
        {
            if (stage == SecretFileStage.BeforeReplace && !File.Exists(Path.Combine(_directory, input)))
            {
                ready.SignalAndWait(TimeSpan.FromSeconds(10)).ShouldBeTrue();
            }
        });
        using var first = new EncryptedFileSecretStore(_directory, files, () => null);
        using var second = new EncryptedFileSecretStore(_directory, files, () => null);
        await Task.WhenAll(Task.Run(() => first.SetAsync("key", "value", Ct), Ct),
            Task.Run(() => second.SetAsync("key", "value", Ct), Ct));
        first.KeyForTests.ShouldBe(second.KeyForTests);
        var before = File.ReadAllText(Path.Combine(_directory, input));
        using var restarted = new EncryptedFileSecretStore(_directory, systemMachineId: () => null);
        (await restarted.GetAsync("key", Ct)).ShouldBe("value");
        File.ReadAllText(Path.Combine(_directory, input)).ShouldBe(before);
        SecureSecretFileTests.AssertPrivate(Path.Combine(_directory, input), false);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("bad-machine-id")]
    public async Task Missing_or_corrupt_generated_id_is_not_recreated_even_with_a_cached_key(string? replacement)
    {
        using var store = new EncryptedFileSecretStore(_directory, systemMachineId: () => null);
        await store.SetAsync("key", "old", Ct);
        var path = Path.Combine(_directory, "machine.id");
        if (replacement is null) { File.Delete(path); }
        else { File.WriteAllText(path, replacement); }
        _ = await Should.ThrowAsync<SecretStorageException>(() => store.SetAsync("key", "new", Ct));
        using var reopened = new EncryptedFileSecretStore(_directory, systemMachineId: () => null);
        _ = await Should.ThrowAsync<SecretStorageException>(() => reopened.GetAsync("key", Ct));
        if (replacement is null) { File.Exists(path).ShouldBeFalse(); }
        else { File.ReadAllText(path).ShouldBe(replacement); }
    }

    [Fact]
    public async Task Reads_ignore_abandoned_files_and_keep_system_machine_id_sources_read_only()
    {
        var system = Path.Combine(_directory, "system-id");
        _ = Directory.CreateDirectory(_directory);
        File.WriteAllText(system, "0123456789abcdef0123456789abcdef");
        var mode = OperatingSystem.IsWindows() ? default : File.GetUnixFileMode(system);
        using var store = new EncryptedFileSecretStore(_directory, systemMachineId: () => File.ReadAllText(system));
        await store.SetAsync("key", "old", Ct);
        File.WriteAllText(Path.Combine(_directory, ".secret-abandoned.tmp"), "broken");
        File.WriteAllText(Path.Combine(_directory, "secrets.json.tmp"), "also broken");
        (await store.GetAsync("key", Ct)).ShouldBe("old");
        File.Exists(Path.Combine(_directory, "machine.id")).ShouldBeFalse();
        File.ReadAllText(system).ShouldBe("0123456789abcdef0123456789abcdef");
        if (!OperatingSystem.IsWindows()) { File.GetUnixFileMode(system).ShouldBe(mode); }
    }

    [Fact]
    public async Task Existing_v1_ciphertext_and_key_inputs_keep_their_format_and_derivation()
    {
        const string machine = "0123456789abcdef0123456789abcdef";
        var salt = Enumerable.Range(0, 32).Select(i => (byte)i).ToArray();
        var user = OperatingSystem.IsLinux() || OperatingSystem.IsMacOS()
            ? BitConverter.GetBytes(getuid()) : Encoding.UTF8.GetBytes(Environment.UserName);
        var key = HKDF.DeriveKey(HashAlgorithmName.SHA256, [.. Encoding.UTF8.GetBytes(machine), .. user], 32,
            salt, "GrpCurl.Net Studio secret store v1"u8.ToArray());
        var nonce = new byte[12];
        var tag = new byte[16];
        var plaintext = Encoding.UTF8.GetBytes("legacy-secret");
        var cipher = new byte[plaintext.Length];
        using (var aes = new AesGcm(key, 16)) { aes.Encrypt(nonce, plaintext, cipher, tag); }
        var blob = $"{Convert.ToBase64String(nonce)}.{Convert.ToBase64String(tag)}.{Convert.ToBase64String(cipher)}";
        _ = Directory.CreateDirectory(_directory);
        File.WriteAllText(Path.Combine(_directory, "machine.id"), machine);
        File.WriteAllText(Path.Combine(_directory, "secrets.salt"), Convert.ToBase64String(salt));
        File.WriteAllText(Path.Combine(_directory, "secrets.json"), JsonSerializer.Serialize(new Dictionary<string, string> { ["legacy"] = blob }));
        using var store = new EncryptedFileSecretStore(_directory, systemMachineId: () => null);
        (await store.GetAsync("legacy", Ct)).ShouldBe("legacy-secret");
        await store.SetAsync("new", "new-secret", Ct);
        var saved = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(Path.Combine(_directory, "secrets.json")))!;
        saved["legacy"].ShouldBe(blob);
        saved["new"].Split('.').Length.ShouldBe(3);
        File.ReadAllText(Path.Combine(_directory, "machine.id")).ShouldBe(machine);
        File.ReadAllText(Path.Combine(_directory, "secrets.salt")).ShouldBe(Convert.ToBase64String(salt));
    }

    [Fact]
    public async Task Environment_editor_reports_actionable_sanitized_storage_failure_and_retains_input()
    {
        var files = new SecureSecretFile((stage, _) =>
        {
            if (stage == SecretFileStage.BeforeDirectoryProtection) { throw new UnauthorizedAccessException("canary"); }
        });
        using var store = new SecretStore(_directory, null, new EncryptedFileSecretStore(_directory, files), new(SecretFallbackMode.Allow));
        var vm = new EnvironmentEditorViewModel(store) { Name = "test" };
        vm.AddVariableCommand.Execute(null);
        vm.Variables[0].Name = "TOKEN";
        vm.Variables[0].IsSecret = true;
        vm.Variables[0].Value = "canary";
        await vm.SaveCommand.ExecuteAsync(null);
        _ = vm.SaveError.ShouldNotBeNull();
        vm.SaveError.ShouldContain("permissions");
        vm.SaveError.ShouldNotContain("canary");
        vm.Variables[0].Value.ShouldBe("canary");
        File.Exists(Path.Combine(_directory, "secrets.json")).ShouldBeFalse();
    }

    [Fact]
    public async Task Storage_failure_during_probe_never_authorizes_fallback_even_with_allow_policy()
    {
        var native = new FailingBackend();
        using var fallback = new EncryptedFileSecretStore(_directory);
        using var store = new SecretStore(_directory, native, fallback, new(SecretFallbackMode.Allow));
        store.State.CanEnableFallback.ShouldBeFalse();
        store.State.Message.ShouldContain("repair permissions");
        _ = await Should.ThrowAsync<SecretBackendUnavailableException>(() => store.EnableFallbackAsync(Ct));
        _ = await Should.ThrowAsync<SecretBackendUnavailableException>(() => store.SetAsync("key", "value", Ct));
        Directory.Exists(_directory).ShouldBeFalse();
    }

    [Fact]
    public async Task Constructors_and_empty_reads_create_no_files()
    {
        using var fallback = new EncryptedFileSecretStore(_directory);
        using var store = new SecretStore(_directory, null, fallback);
        (await store.ListAsync(Ct)).ShouldBeEmpty();
        (await fallback.GetAsync("absent", Ct)).ShouldBeNull();
        if (OperatingSystem.IsWindows())
        {
            using var dpapi = new WindowsDpapiSecretStore(_directory);
            (await dpapi.ExistsAsync("absent", Ct)).ShouldBeFalse();
        }
        Directory.Exists(_directory).ShouldBeFalse();
    }

    private sealed class FailingBackend : ISecretBackend
    {
        public SecretStoreInfo Info { get; } = new("Native", true, null);
        public Task<bool> ExistsAsync(string keyRef, CancellationToken cancellationToken = default)
            => throw new SecretStorageException("Please repair permissions.");
        public Task SetAsync(string keyRef, string value, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<string?> GetAsync(string keyRef, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task DeleteAsync(string keyRef, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    }

    [DllImport("libc")]
    private static extern uint getuid();
}
