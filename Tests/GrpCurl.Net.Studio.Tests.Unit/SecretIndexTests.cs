using GrpCurl.Net.Studio.Services.Secrets;
using GrpCurl.Net.Studio.ViewModels.Connections;
using GrpCurl.Net.Studio.ViewModels.Models;
using GrpCurl.Net.Studio.ViewModels.Services;
using System.Text;

namespace GrpCurl.Net.Studio.Tests.Unit;

// Uses APIs already present at e925e8e so the strict-index regressions can run unchanged there.
public sealed class SecretIndexTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "grpcn-prd039-index-" + Guid.NewGuid().ToString("N"));
    private string IndexPath => Path.Combine(_directory, "secret-keyrefs.json");
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose()
    {
        if (Directory.Exists(_directory)) { Directory.Delete(_directory, true); }
    }

    private void WriteIndex(string json)
    {
        _ = Directory.CreateDirectory(_directory);
        File.WriteAllText(IndexPath, json);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("[]")]
    public async Task Missing_or_empty_index_supports_first_use(string? json)
    {
        if (json is not null) { WriteIndex(json); }
        using var store = new SecretStore(_directory, new Backend(), new Backend());
        (await store.ListAsync(Ct)).ShouldBeEmpty();
        File.Exists(IndexPath).ShouldBe(json is not null);
        await store.SetAsync("first", "value", Ct);
        (await store.ListAsync(Ct)).ShouldBe(["first"]);
        await store.DeleteAsync("first", Ct);
        (await store.ListAsync(Ct)).ShouldBeEmpty();
    }

    [Fact]
    public async Task Valid_legacy_references_preserve_text_order_and_ordinal_identity()
    {
        const string json = "[ \"studio/v1/legacy\", \" key \", \"Key\", \"key\", \"é\", \"e\\u0301\" ]";
        WriteIndex(json);
        using var store = new SecretStore(_directory, new Backend(), new Backend());
        var expected = new[] { "studio/v1/legacy", " key ", "Key", "key", "é", "e\u0301" };
        (await store.ListAsync(Ct)).ShouldBe(expected);
        File.ReadAllText(IndexPath).ShouldBe(json);
        await store.SetAsync(" key ", "value", Ct);
        File.ReadAllText(IndexPath).ShouldBe(json); // no read-time rewrite or duplicate on update
        await store.DeleteAsync("Key", Ct);
        (await store.ListAsync(Ct)).ShouldBe(expected.Where(key => key != "Key").ToArray());
        await store.SetAsync(" new ", "value", Ct);
        (await store.ListAsync(Ct)).Last().ShouldBe(" new ");
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("broken-canary")]
    [InlineData("[\"credential-canary\"")]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("\"credential-canary\"")]
    [InlineData("42")]
    [InlineData("true")]
    [InlineData("[null]")]
    [InlineData("[1]")]
    [InlineData("[false]")]
    [InlineData("[{}]")]
    [InlineData("[[]]")]
    [InlineData("[\"\"]")]
    [InlineData("[\" \\t\\r\\n\"]")]
    [InlineData("[\"credential-canary\",\"credential-canary\"]")]
    [InlineData("[\"key\",\"\\u006bey\"]")]
    [InlineData("[\"key\",]")]
    public async Task Invalid_index_fails_listing_and_prevents_native_mutations_without_rewriting(string json)
    {
        WriteIndex(json);
        var before = File.ReadAllBytes(IndexPath);
        var native = new Backend();
        var fallback = new Backend();
        var logs = new List<string>();
        using var store = new SecretStore(_directory, native, fallback, new(SecretFallbackMode.Allow), log: logs.Add);
        var error = await Should.ThrowAsync<SecretStorageException>(() => store.ListAsync(Ct));
        error.Message.ShouldContain("Close Studio");
        error.Message.ShouldContain("backup");
        error.Message.ShouldNotContain("canary");
        error.Message.ShouldNotContain("BytePosition");
        error.InnerException.ShouldBeNull();
        _ = await Should.ThrowAsync<SecretStorageException>(() => store.SetAsync("key", "secret-canary", Ct));
        _ = await Should.ThrowAsync<SecretStorageException>(() => store.DeleteAsync("key", Ct));
        native.Mutations.ShouldBe(0);
        fallback.Mutations.ShouldBe(0);
        File.ReadAllBytes(IndexPath).ShouldBe(before);
        store.Info.IsOsKeychain.ShouldBeTrue();
        store.State.CanEnableFallback.ShouldBeFalse();
        string.Join(" ", logs).ShouldNotContain("canary");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" \t\r\n")]
    public async Task Invalid_mutation_reference_is_rejected_before_any_files_or_backend_mutations(string? keyRef)
    {
        var native = new Backend();
        using var store = new SecretStore(_directory, native, new Backend());
        _ = await Should.ThrowAsync<ArgumentException>(() => store.SetAsync(keyRef!, "value", Ct));
        _ = await Should.ThrowAsync<ArgumentException>(() => store.DeleteAsync(keyRef!, Ct));
        native.Mutations.ShouldBe(0);
        Directory.Exists(_directory).ShouldBeFalse();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Corruption_after_backend_commit_is_preserved_and_reported_as_partial(bool delete)
    {
        var native = new Backend();
        using var store = new SecretStore(_directory, native, new Backend());
        await store.SetAsync("key", "old", Ct);
        const string corrupt = "[\"credential-canary\",null]";
        native.AfterMutation = async () =>
        {
            // Reentrancy also proves the preflight index gate is released before backend calls.
            (await store.ListAsync(Ct).WaitAsync(TimeSpan.FromSeconds(5), Ct)).ShouldBe(["key"]);
            WriteIndex(corrupt);
        };
        var error = await Should.ThrowAsync<SecretStorageException>(() => delete
            ? store.DeleteAsync("key", Ct) : store.SetAsync("key", "new", Ct));
        error.Message.ShouldContain("secret change completed");
        error.Message.ShouldContain("Close Studio");
        error.Message.ShouldNotContain("canary");
        File.ReadAllBytes(IndexPath).ShouldBe(Encoding.UTF8.GetBytes(corrupt));
        (await store.GetAsync("key", Ct)).ShouldBe(delete ? null : "new");
        (await store.ExistsAsync("key", Ct)).ShouldBe(!delete);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Index_persistence_failure_after_backend_commit_retains_previous_index(bool delete)
    {
        var fail = false;
        var files = new SecureSecretFile((stage, _) =>
        {
            if (fail && stage == SecretFileStage.PartialWrite) { throw new IOException("raw-canary"); }
        });
        using var store = new SecretStore(_directory, new Backend(), new Backend(), files: files);
        await store.SetAsync("key", "old", Ct);
        var before = File.ReadAllBytes(IndexPath);
        fail = true;
        var error = await Should.ThrowAsync<SecretStorageException>(() => delete
            ? store.DeleteAsync("key", Ct) : store.SetAsync("second", "new", Ct));
        error.Message.ShouldContain("secret change completed");
        error.Message.ShouldNotContain("canary");
        File.ReadAllBytes(IndexPath).ShouldBe(before);
        (await store.GetAsync(delete ? "key" : "second", Ct)).ShouldBe(delete ? null : "new");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Readable_secrets_survive_corrupt_index_but_listing_and_fallback_missing_decisions_fail(bool useFallback)
    {
        var backend = new Backend();
        using var store = new SecretStore(_directory, useFallback ? null : backend,
            useFallback ? backend : new Backend(), new(SecretFallbackMode.Allow));
        await store.SetAsync("key", "value", Ct);
        WriteIndex("broken");
        (await store.GetAsync("key", Ct)).ShouldBe("value");
        (await store.ExistsAsync("key", Ct)).ShouldBeTrue();
        _ = await Should.ThrowAsync<SecretStorageException>(() => store.ListAsync(Ct));
        if (useFallback)
        {
            _ = await Should.ThrowAsync<SecretStorageException>(() => store.GetAsync("absent", Ct));
            _ = await Should.ThrowAsync<SecretStorageException>(() => store.ExistsAsync("absent", Ct));
        }
        else
        {
            (await store.GetAsync("absent", Ct)).ShouldBeNull();
            (await store.ExistsAsync("absent", Ct)).ShouldBeFalse();
        }
        foreach (var reference in new[] { "key", "absent" })
        {
            _ = await Should.ThrowAsync<SecretStorageException>(() => store.SetAsync(reference, "new", Ct));
            _ = await Should.ThrowAsync<SecretStorageException>(() => store.DeleteAsync(reference, Ct));
        }
        backend.Mutations.ShouldBe(1);
        File.ReadAllText(IndexPath).ShouldBe("broken");
    }

    [Fact]
    public async Task Environment_editor_retains_input_and_reports_sanitized_backup_recovery_without_downgrading()
    {
        WriteIndex("[\"credential-canary\",null]");
        var native = new Backend();
        var fallback = new Backend();
        using var store = new SecretStore(_directory, native, fallback, new(SecretFallbackMode.Allow));
        var vm = new EnvironmentEditorViewModel(store) { Name = "test" };
        vm.AddVariableCommand.Execute(null);
        vm.Variables[0].Name = "TOKEN";
        vm.Variables[0].IsSecret = true;
        vm.Variables[0].Value = "secret-canary";
        await vm.SaveCommand.ExecuteAsync(null);
        var error = vm.SaveError.ShouldNotBeNull();
        error.ShouldContain("Close Studio");
        error.ShouldContain("backup");
        error.ShouldNotContain("canary");
        vm.Variables[0].Value.ShouldBe("secret-canary");
        native.Mutations.ShouldBe(0);
        fallback.Mutations.ShouldBe(0);
        store.Info.IsOsKeychain.ShouldBeTrue();
        store.State.CanEnableFallback.ShouldBeFalse();
    }

    private sealed class Backend : ISecretBackend
    {
        private readonly Dictionary<string, string> _values = new(StringComparer.Ordinal);
        public SecretStoreInfo Info { get; } = new("Test native", true, null);
        public int Mutations { get; private set; }
        public Func<Task>? AfterMutation { get; set; }
        public async Task SetAsync(string keyRef, string value, CancellationToken cancellationToken = default)
        {
            _values[keyRef] = value;
            Mutations++;
            if (AfterMutation is not null) { await AfterMutation(); }
        }
        public async Task DeleteAsync(string keyRef, CancellationToken cancellationToken = default)
        {
            _ = _values.Remove(keyRef);
            Mutations++;
            if (AfterMutation is not null) { await AfterMutation(); }
        }
        public Task<string?> GetAsync(string keyRef, CancellationToken cancellationToken = default)
            => Task.FromResult(_values.GetValueOrDefault(keyRef));
        public Task<bool> ExistsAsync(string keyRef, CancellationToken cancellationToken = default)
            => Task.FromResult(_values.ContainsKey(keyRef));
    }
}
