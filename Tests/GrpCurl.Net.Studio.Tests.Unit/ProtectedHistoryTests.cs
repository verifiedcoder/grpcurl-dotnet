using GrpCurl.Net.Studio.Services;
using GrpCurl.Net.Studio.Services.Secrets;
using GrpCurl.Net.Studio.TestSupport;
using GrpCurl.Net.Studio.Tests.Unit.Fakes;
using GrpCurl.Net.Studio.ViewModels.Models.History;
using GrpCurl.Net.Studio.ViewModels.Services;
using System.Security.Cryptography;
using System.Text.Json;

namespace GrpCurl.Net.Studio.Tests.Unit;

public sealed class ProtectedHistoryTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "grpcn-prd007-" + Guid.NewGuid().ToString("N"));
    private readonly FakeSecretStore _secrets = new();
    private readonly FakeSettingsStore _settings = new();
    private string Path_ => Path.Combine(_directory, "history.ndjson");
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    public void Dispose() { if (Directory.Exists(_directory)) { Directory.Delete(_directory, true); } }
    private JsonHistoryStore Store(IHistoryPolicy? policy = null, SecureSecretFile? files = null, Action<string, byte[]>? observe = null)
        => new(Path_, _secrets, _settings, policy, files: files, observe: observe);
    private static HistoryEntry Entry(string body = "{\"password\":\"canary\",\"nested\":[{\"private\":\"canary\"}],\"keep\":42}", string format = "json") => new(
        1, Guid.NewGuid().ToString("N"), DateTimeOffset.UtcNow, format == "graphql" ? HistoryKind.Graphql : HistoryKind.Grpc,
        new("test", "localhost:1", "tls", null), null, "Svc/Call",
        new(format, body, false, [new("authorization", "canary")], null, false, false, null, null, null),
        new("Error", "rpc-error", 1, 1, 1, 0, "{\"password\":\"canary\"}", false, "error-canary"));

    [Fact]
    public async Task Default_capture_omits_payloads_even_for_direct_store_callers()
    {
        using var store = Store();
        await store.AppendAsync(Entry(), Ct);
        var entry = (await store.ReadAllAsync(Ct)).Single();
        entry.Request.Body.ShouldBeEmpty();
        entry.Request.BodyDisposition.ShouldBe("omitted");
        entry.Outcome.ResponseBody.ShouldBeNull();
        entry.Outcome.ErrorMessage.ShouldBeNull();
        JsonSerializer.Serialize(entry).ShouldNotContain("canary");
        File.ReadAllText(Path_).ShouldStartWith("enc1:");
        File.ReadAllText(Path_).ShouldNotContain("canary");
        SecureSecretFileTests.AssertPrivate(Path_, false);
        SecureSecretFileTests.AssertPrivate(_directory, true);
        SecureSecretFileTests.AssertPrivate(Path_ + ".lock", false);
    }

    [Fact]
    public async Task Opted_in_json_is_recursively_redacted_before_capping_and_export()
    {
        _settings.Current.History.CaptureRequestBodies = true;
        _settings.Current.History.CaptureResponses = true;
        _settings.Current.History.RedactFields = ["PRIVATE"];
        using var store = Store();
        await store.AppendAsync(Entry() with { Outcome = Entry().Outcome with { ErrorMessage = null } }, Ct);
        var entries = await store.ReadAllAsync(Ct);
        entries[0].Request.Body.ShouldContain("42");
        entries[0].Request.BodyDisposition.ShouldBe("redacted");
        JsonSerializer.Serialize(entries).ShouldNotContain("canary");
        var exports = Directory.CreateDirectory(Path.Combine(_directory, "exports")).FullName;
        var export = Path.Combine(exports, "shared.json");
        await store.ExportAsync(export, entries, Ct);
        File.ReadAllText(export).ShouldStartWith("{");
        File.ReadAllText(export).ShouldNotContain("canary");
        SecureSecretFileTests.AssertPrivate(export, false);
        _ = await Should.ThrowAsync<HistoryStorageException>(() => store.ExportAsync(Path_, entries, Ct));
    }

    [Theory]
    [InlineData("query Q($v:String=\"canary\") { f(a:123,b:1.25,c:true,d:SECRET,e:null,f:[\"canary\"],g:{x:SECRET},v:$v) } # canary")]
    [InlineData("# canary\nquery Q { f(a: # canary\n \"canary\") } # canary")]
    [InlineData("query Q { f(a: \"\"\"canary\"\"\") }")]
    public async Task Graphql_literals_defaults_and_comments_are_removed(string document)
    {
        _settings.Current.History.CaptureRequestBodies = true;
        using var store = Store();
        await store.AppendAsync(Entry(document, "graphql"), Ct);
        var entry = (await store.ReadAllAsync(Ct)).Single();
        entry.Request.Body.ShouldNotBeEmpty();
        entry.Request.Body.ShouldNotContain("canary");
        entry.Request.Body.ShouldNotContain("SECRET");
        entry.Request.Body.ShouldNotContain("123");
        entry.Request.Body.ShouldNotContain("true");
        entry.Request.BodyDisposition.ShouldBe("redacted");
        if (document.Contains("$v")) { entry.Request.Body.ShouldContain("$v"); }
    }

    [Theory]
    [InlineData("not JSON", "json")]
    [InlineData("{\"password\":\"a\",\"password\":\"b\"}", "json")]
    [InlineData("password: canary", "text")]
    [InlineData("query { f(", "graphql")]
    public async Task Unsupported_or_malformed_payload_is_omitted(string body, string format)
    {
        _settings.Current.History.CaptureRequestBodies = true;
        using var store = Store();
        await store.AppendAsync(Entry(body, format), Ct);
        (await store.ReadAllAsync(Ct)).Single().Request.BodyDisposition.ShouldBe("omitted");
    }

    [Fact]
    public async Task Utf8_cap_is_applied_after_redaction()
    {
        _settings.Current.History.CaptureRequestBodies = true;
        _settings.Current.History.ResponseCapBytes = 8;
        using var store = Store();
        await store.AppendAsync(Entry("\"密碼🔐abcdef\""), Ct);
        var entry = (await store.ReadAllAsync(Ct)).Single();
        entry.Request.Body.ShouldBe("\"密碼");
        entry.Request.BodyTruncated.ShouldBeTrue();
    }

    [Fact]
    public async Task Legacy_and_mixed_history_migrate_atomically_preserving_existing_payloads_and_pins()
    {
        _ = Directory.CreateDirectory(_directory);
        var legacy = Entry() with { Pinned = true };
        File.WriteAllText(Path_, JsonSerializer.Serialize(legacy, HistoryJsonContext.Default.HistoryEntry) + "\n");
        using var store = Store();
        var restored = (await store.ReadAllAsync(Ct)).Single();
        restored.Request.Body.ShouldBe(legacy.Request.Body);
        restored.Pinned.ShouldBeTrue();
        File.ReadAllText(Path_).ShouldStartWith("enc1:");
        File.ReadAllText(Path_).ShouldNotContain("canary");
        File.AppendAllText(Path_, JsonSerializer.Serialize(Entry(), HistoryJsonContext.Default.HistoryEntry) + "\n");
        (await store.ReadAllAsync(Ct)).Count.ShouldBe(2);
        File.ReadAllLines(Path_).ShouldAllBe(line => line.StartsWith("enc1:"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Failed_migration_obeys_commit_point(bool afterCommit)
    {
        _ = Directory.CreateDirectory(_directory);
        var original = JsonSerializer.Serialize(Entry(), HistoryJsonContext.Default.HistoryEntry) + "\n";
        File.WriteAllText(Path_, original);
        var files = new SecureSecretFile((stage, _) =>
        {
            if (stage == (afterCommit ? SecretFileStage.Replaced : SecretFileStage.PartialWrite)) { throw new IOException("canary"); }
        });
        using var store = Store(files: files);
        var error = await Should.ThrowAsync<HistoryStorageException>(() => store.ReadAllAsync(Ct));
        error.ToString().ShouldNotContain("canary");
        if (afterCommit) { File.ReadAllText(Path_).ShouldStartWith("enc1:"); }
        else { File.ReadAllText(Path_).ShouldBe(original); }
        Directory.GetFiles(_directory, "*.tmp").ShouldBeEmpty();
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("wrong")]
    [InlineData("malformed")]
    [InlineData("corrupt")]
    public async Task Key_or_authentication_failure_never_regenerates_or_rewrites_and_clear_all_recovers(string failure)
    {
        using var store = Store();
        await store.AppendAsync(Entry(), Ct);
        if (failure == "missing") { await _secrets.DeleteAsync(HistoryCipher.KeyReference, Ct); }
        if (failure == "wrong") { await _secrets.SetAsync(HistoryCipher.KeyReference, Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)), Ct); }
        if (failure == "malformed") { await _secrets.SetAsync(HistoryCipher.KeyReference, "bad", Ct); }
        if (failure == "corrupt")
        {
            var bytes = Convert.FromBase64String(File.ReadAllText(Path_)[5..].Trim());
            bytes[^1] ^= 1;
            File.WriteAllText(Path_, "enc1:" + Convert.ToBase64String(bytes)); // no newline must still fail auth
        }
        var before = File.ReadAllText(Path_);
        var keyBefore = await _secrets.GetAsync(HistoryCipher.KeyReference, Ct);
        _ = await Should.ThrowAsync<HistoryStorageException>(() => store.ReadAllAsync(Ct));
        _ = await Should.ThrowAsync<HistoryStorageException>(() => store.AppendAsync(Entry(), Ct));
        _ = await Should.ThrowAsync<HistoryStorageException>(() => store.ClearAsync(true, Ct));
        File.ReadAllText(Path_).ShouldBe(before);
        (await _secrets.GetAsync(HistoryCipher.KeyReference, Ct)).ShouldBe(keyBefore);
        await store.ClearAsync(false, Ct);
        File.Exists(Path_).ShouldBeFalse();
        (await _secrets.GetAsync(HistoryCipher.KeyReference, Ct)).ShouldBe(keyBefore);
    }

    [Fact]
    public async Task Truncated_tail_is_removed_before_append_but_complete_corruption_is_preserved()
    {
        using var store = Store();
        await store.AppendAsync(Entry(), Ct);
        File.AppendAllText(Path_, "enc1:incomplete");
        await store.AppendAsync(Entry(), Ct);
        (await store.ReadAllAsync(Ct)).Count.ShouldBe(2);
        File.AppendAllText(Path_, "broken\n");
        var before = File.ReadAllText(Path_);
        _ = await Should.ThrowAsync<HistoryStorageException>(() => store.ReadAllAsync(Ct));
        File.ReadAllText(Path_).ShouldBe(before);
    }

    [Fact]
    public async Task Two_instances_share_key_and_serialize_initialization_and_updates()
    {
        using var first = Store();
        using var second = Store();
        await Task.WhenAll(Task.Run(() => first.AppendAsync(Entry(), Ct), Ct), Task.Run(() => second.AppendAsync(Entry(), Ct), Ct));
        (await first.ReadAllAsync(Ct)).Count.ShouldBe(2);
        (await second.ReadAllAsync(Ct)).Count.ShouldBe(2);
    }

    [Fact]
    public async Task Lock_timeout_cancellation_and_disposal_are_safe()
    {
        using var handle = new SecureSecretFile().TryOpenExclusive(Path_ + ".lock").ShouldNotBeNull();
        using var store = new JsonHistoryStore(Path_, _secrets, lockTimeout: TimeSpan.FromMilliseconds(120));
        var error = await Should.ThrowAsync<HistoryStorageException>(() => store.ReadAllAsync(Ct));
        error.Message.ShouldContain("busy");
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        _ = await Should.ThrowAsync<OperationCanceledException>(() => store.ReadAllAsync(canceled.Token));
        var admitted = store.AppendAsync(Entry(), Ct);
        store.Dispose();
        handle.Dispose();
        await admitted;
        _ = await Should.ThrowAsync<ObjectDisposedException>(() => store.ReadAllAsync(Ct));
    }

    [Theory]
    [InlineData("encrypt")]
    [InlineData("decrypt")]
    [InlineData("decryptAllocated")]
    [InlineData("key")]
    public async Task Actual_owned_buffers_are_cleared_on_success_and_injected_failure(string failAt)
    {
        var captured = new List<byte[]>();
        var nonzero = new HashSet<string>();
        var fail = false;
        using var store = Store(observe: (stage, buffer) =>
        {
            captured.Add(buffer);
            if (buffer.Any(b => b != 0)) { _ = nonzero.Add(stage); }
            if (fail && stage == failAt)
            {
                buffer.AsSpan().Fill(0xa5);
                throw new CryptographicException("canary");
            }
        });
        await store.AppendAsync(Entry(), Ct);
        _ = await store.ReadAllAsync(Ct);
        captured.ShouldNotBeEmpty();
        nonzero.ShouldContain("key");
        nonzero.ShouldContain("encrypt");
        nonzero.ShouldContain("decrypt");
        captured.ShouldAllBe(buffer => buffer.All(b => b == 0));
        fail = true;
        _ = await Should.ThrowAsync<HistoryStorageException>(() => failAt == "encrypt" ? store.AppendAsync(Entry(), Ct) : store.ReadAllAsync(Ct));
        captured.ShouldAllBe(buffer => buffer.All(b => b == 0));
    }

    [Theory]
    [InlineData("{}", true)]
    [InlineData("{\"history\":{\"enabled\":false}}", false)]
    [InlineData("{\"history\":{\"captureRequestBodies\":false,\"captureResponses\":false}}", true)]
    [InlineData("{\"history\":null}", false)]
    [InlineData("{\"history\":{\"enabled\":true,\"enabled\":true}}", false)]
    [InlineData("{\"history\":{\"unknown\":false}}", false)]
    [InlineData("broken-canary", false)]
    public async Task Policy_is_strict_and_enforced_at_storage_without_mutating_preferences(string json, bool enabled)
    {
        _ = Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, "policy.json");
        File.WriteAllText(path, json);
        var policy = HistoryPolicy.Load(path);
        policy.AllowHistory.ShouldBe(enabled);
        _settings.Current.History.CaptureRequestBodies = true;
        _settings.Current.History.CaptureResponses = true;
        using var store = Store(policy);
        await store.AppendAsync(Entry(), Ct);
        File.Exists(Path_).ShouldBe(enabled);
        _settings.Current.History.Enabled.ShouldBeTrue();
        _settings.Current.History.CaptureRequestBodies.ShouldBeTrue();
        if (enabled && !policy.AllowRequests)
        {
            var entry = (await store.ReadAllAsync(Ct)).Single();
            entry.Request.Body.ShouldBeEmpty();
            entry.Outcome.ErrorMessage.ShouldBeNull();
            entry.Outcome.ResponseBody.ShouldBeNull();
        }
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task History_does_not_authorize_fallback_and_honors_block_on_restart(bool consent)
    {
        _settings.Current.Security.SecretFallback = consent
            ? GrpCurl.Net.Studio.ViewModels.Models.SecretFallbackMode.Allow
            : GrpCurl.Net.Studio.ViewModels.Models.SecretFallbackMode.RequireConsent;
        using var secrets = new SecretStore(_directory, null, new EncryptedFileSecretStore(_directory), settings: _settings);
        using var store = new JsonHistoryStore(Path_, secrets, _settings);
        if (!consent)
        {
            _ = await Should.ThrowAsync<HistoryStorageException>(() => store.AppendAsync(Entry(), Ct));
            _ = await Should.ThrowAsync<HistoryStorageException>(() => store.ReadAllAsync(Ct));
            store.Message.ShouldNotBeNull().ShouldContain("Restore secret storage access");
            File.Exists(Path_).ShouldBeFalse();
            File.Exists(Path.Combine(_directory, "secrets.json")).ShouldBeFalse();
            secrets.State.CanEnableFallback.ShouldBeTrue();
            return;
        }
        await store.AppendAsync(Entry(), Ct);
        _ = await store.ReadAllAsync(Ct); // populate cache before revocation
        var before = File.ReadAllText(Path_);
        _settings.Current.Security.SecretFallback = GrpCurl.Net.Studio.ViewModels.Models.SecretFallbackMode.Block;
        using var blockedSecrets = new SecretStore(_directory, null, new EncryptedFileSecretStore(_directory), settings: _settings);
        using var blockedStore = new JsonHistoryStore(Path_, blockedSecrets, _settings);
        _ = await Should.ThrowAsync<HistoryStorageException>(() => blockedStore.ReadAllAsync(Ct));
        _ = await Should.ThrowAsync<HistoryStorageException>(() => blockedStore.ExportAsync(Path.Combine(_directory, "..", Guid.NewGuid()+".json"), [Entry()], Ct));
        File.ReadAllText(Path_).ShouldBe(before);
        await store.ClearAsync(false, Ct);
        File.Exists(Path_).ShouldBeFalse();
    }

    [Theory]
    [InlineData("{oops")]
    [InlineData("{}")]
    [InlineData("null")]
    [InlineData("enc1:!invalid")]
    public async Task Malformed_unterminated_tail_is_preserved_unless_it_is_actually_incomplete(string tail)
    {
        using var store = Store();
        await store.AppendAsync(Entry(), Ct);
        File.AppendAllText(Path_, tail);
        var before = File.ReadAllText(Path_);
        _ = await Should.ThrowAsync<HistoryStorageException>(() => store.ReadAllAsync(Ct));
        File.ReadAllText(Path_).ShouldBe(before);
    }

    [Fact]
    public async Task Separate_processes_serialize_first_key_creation_and_leave_readable_history()
    {
        const string marker = "GRPCN_HISTORY_TEST_DIRECTORY";
        if (Environment.GetEnvironmentVariable(marker) is { Length: > 0 } shared)
        {
            using var secrets = new SecretStore(shared, null, new EncryptedFileSecretStore(shared),
                new(GrpCurl.Net.Studio.ViewModels.Models.SecretFallbackMode.Allow));
            using var store = new JsonHistoryStore(Path.Combine(shared, "history.ndjson"), secrets);
            File.WriteAllText(Path.Combine(shared, Environment.ProcessId + ".ready"), "ready");
            await store.AppendAsync(Entry(), Ct);
            return;
        }
        using var held = new SecureSecretFile().TryOpenExclusive(Path_ + ".lock").ShouldNotBeNull();
        var children = new List<System.Diagnostics.Process>();
        var output = new List<Task<string>>();
        try
        {
            for (var i = 0; i < 2; i++)
            {
                var start = new System.Diagnostics.ProcessStartInfo("dotnet")
                { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
                start.ArgumentList.Add(typeof(ProtectedHistoryTests).Assembly.Location);
                start.ArgumentList.Add("-method");
                start.ArgumentList.Add($"{typeof(ProtectedHistoryTests).FullName}.{nameof(Separate_processes_serialize_first_key_creation_and_leave_readable_history)}");
                start.ArgumentList.Add("-noAutoReporters");
                start.ArgumentList.Add("-noColor");
                start.Environment[marker] = _directory;
                var child = System.Diagnostics.Process.Start(start).ShouldNotBeNull();
                children.Add(child);
                output.Add(child.StandardOutput.ReadToEndAsync(Ct));
                output.Add(child.StandardError.ReadToEndAsync(Ct));
            }
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(Ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(30));
            while (Directory.GetFiles(_directory, "*.ready").Length != 2) { await Task.Delay(20, timeout.Token); }
            File.Exists(Path_).ShouldBeFalse("both processes must wait behind the held lock");
            held.Dispose();
            foreach (var child in children) { await child.WaitForExitAsync(timeout.Token); }
            var diagnostics = string.Join("\n", await Task.WhenAll(output));
            children.ShouldAllBe(p => p.ExitCode == 0, diagnostics);
            using var secrets = new SecretStore(_directory, null, new EncryptedFileSecretStore(_directory),
                new(GrpCurl.Net.Studio.ViewModels.Models.SecretFallbackMode.Allow));
            using var store = new JsonHistoryStore(Path_, secrets);
            (await store.ReadAllAsync(Ct)).Count.ShouldBe(2);
            (await secrets.ListAsync(Ct)).ShouldBe([HistoryCipher.KeyReference]);
        }
        finally
        {
            held.Dispose();
            foreach (var child in children)
            {
                if (!child.HasExited) { child.Kill(entireProcessTree: true); await child.WaitForExitAsync(CancellationToken.None); }
                child.Dispose();
            }
        }
    }

}
