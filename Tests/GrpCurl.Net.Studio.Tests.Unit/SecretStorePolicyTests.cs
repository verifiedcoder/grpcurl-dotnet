using GrpCurl.Net.Studio.ViewModels.Models.Connections;
using GrpCurl.Net.Studio.Services;
using GrpCurl.Net.Studio.Services.Secrets;
using GrpCurl.Net.Studio.Tests.Unit.Fakes;
using GrpCurl.Net.Studio.TestSupport;
using GrpCurl.Net.Studio.ViewModels.Connections;
using GrpCurl.Net.Studio.ViewModels.Documents;
using GrpCurl.Net.Studio.ViewModels.Models;
using GrpCurl.Net.Studio.ViewModels.Services;

namespace GrpCurl.Net.Studio.Tests.Unit;

public sealed class SecretStorePolicyTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "grpcn-policy-" + Guid.NewGuid().ToString("N"));
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [Fact]
    public async Task Unavailable_startup_creates_no_files_without_consent()
    {
        using var fallback = new EncryptedFileSecretStore(_directory);
        using var store = new SecretStore(_directory, new Backend { Failure = Unavailable() }, fallback);
        _ = await Should.ThrowAsync<SecretBackendUnavailableException>(() => store.SetAsync("k", "canary", Ct));
        store.State.CanEnableFallback.ShouldBeTrue();
        Directory.Exists(_directory).ShouldBeFalse();
    }

    [Theory]
    [InlineData("set")]
    [InlineData("get")]
    [InlineData("exists")]
    [InlineData("delete")]
    public async Task Mid_session_unavailability_never_calls_fallback_or_hides_native_references(string operation)
    {
        var native = new Backend();
        var fallback = new Backend();
        var logs = new List<string>();
        using var store = new SecretStore(_directory, native, fallback, new(SecretFallbackMode.Allow), warning: logs.Add);
        await store.SetAsync("native-key", "canary-secret", Ct);
        native.Failure = Unavailable();
        _ = await Should.ThrowAsync<SecretBackendUnavailableException>(() => Run(store, operation));
        fallback.Calls.ShouldBe(0);
        store.Info.IsOsKeychain.ShouldBeTrue();
        (await store.ListAsync(Ct)).ShouldContain("native-key");
        string.Join(" ", logs).ShouldNotContain("canary-secret");
        native.Failure = null;
        (await store.GetAsync("native-key", Ct)).ShouldBe("canary-secret");
        store.State.Message.ShouldBe("Secrets use the native keystore.");
    }

    [Theory]
    [InlineData("set")]
    [InlineData("get")]
    [InlineData("exists")]
    [InlineData("delete")]
    public async Task Generic_operation_errors_never_change_backend(string operation)
    {
        var native = new Backend();
        var fallback = new Backend();
        using var store = new SecretStore(_directory, native, fallback);
        var failure = new InvalidOperationException("application bug");
        native.Failure = failure;
        (await Should.ThrowAsync<InvalidOperationException>(() => Run(store, operation))).ShouldBeSameAs(failure);
        fallback.Calls.ShouldBe(0);
        store.Info.IsOsKeychain.ShouldBeTrue();
    }

    [Fact]
    public async Task Unexpected_probe_failure_does_not_authorize_fallback_even_with_saved_consent()
    {
        var fallback = new Backend();
        using var store = new SecretStore(_directory, new Backend { Failure = new InvalidOperationException("native detail") },
            fallback, new(SecretFallbackMode.Allow));
        store.State.CanEnableFallback.ShouldBeFalse();
        _ = await Should.ThrowAsync<SecretBackendUnavailableException>(() => store.EnableFallbackAsync(Ct));
        fallback.Calls.ShouldBe(0);
        store.State.Message.ShouldNotContain("native detail");
    }

    [Fact]
    public async Task Administrator_block_overrides_saved_consent()
    {
        var settings = new FakeSettingsStore();
        settings.Current.Security.SecretFallback = SecretFallbackMode.Allow;
        var fallback = new Backend();
        using var store = new SecretStore(_directory, null, fallback, new(SecretFallbackMode.Block), settings);
        store.State.CanEnableFallback.ShouldBeFalse();
        _ = await Should.ThrowAsync<SecretBackendUnavailableException>(() => store.EnableFallbackAsync(Ct));
        _ = await Should.ThrowAsync<SecretBackendUnavailableException>(() => store.SetAsync("k", "v", Ct));
        settings.SaveCount.ShouldBe(0);
        fallback.Calls.ShouldBe(0);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Explicit_startup_authorization_allows_fallback_and_records_its_source(bool administrator)
    {
        var settings = new FakeSettingsStore();
        settings.Current.Security.SecretFallback = administrator ? SecretFallbackMode.RequireConsent : SecretFallbackMode.Allow;
        var log = new List<string>();
        using var fallback = new EncryptedFileSecretStore(_directory);
        using var store = new SecretStore(_directory, null, fallback,
            new(administrator ? SecretFallbackMode.Allow : SecretFallbackMode.RequireConsent), settings, warning: log.Add);
        await store.SetAsync("k", "secret", Ct);
        (await store.GetAsync("k", Ct)).ShouldBe("secret");
        store.Info.IsOsKeychain.ShouldBeFalse();
        string.Join(" ", log).ShouldContain(administrator ? "administrator policy" : "saved user consent");
    }

    [Fact]
    public void Native_storage_wins_over_prior_fallback_consent()
    {
        var native = new Backend();
        var fallback = new Backend();
        using var store = new SecretStore(_directory, native, fallback, new(SecretFallbackMode.Allow));
        store.Info.ShouldBe(native.Info);
        fallback.Calls.ShouldBe(0);
    }

    [Fact]
    public async Task Consent_is_persisted_before_fallback_can_receive_a_secret()
    {
        var settings = new FakeSettingsStore();
        var fallback = new Backend();
        using var store = new SecretStore(_directory, null, fallback, settings: settings);
        await store.EnableFallbackAsync(Ct);
        settings.Current.Security.SecretFallback.ShouldBe(SecretFallbackMode.Allow);
        settings.SaveCount.ShouldBe(1);
        fallback.Calls.ShouldBe(0);
        await store.SetAsync("k", "v", Ct);
        fallback.Values["k"].ShouldBe("v");
        store.State.CanEnableFallback.ShouldBeFalse();
    }

    [Fact]
    public async Task Failed_consent_save_changes_neither_settings_nor_backend()
    {
        var settings = new ControlledSettings { FailSave = true };
        var fallback = new Backend();
        using var store = new SecretStore(_directory, null, fallback, settings: settings);
        _ = await Should.ThrowAsync<IOException>(() => store.EnableFallbackAsync(Ct));
        settings.Current.Security.SecretFallback.ShouldBe(SecretFallbackMode.RequireConsent);
        store.State.CanEnableFallback.ShouldBeTrue();
        fallback.Calls.ShouldBe(0);
    }

    [Fact]
    public async Task Retry_recovers_native_storage_without_authorizing_file_storage()
    {
        var native = new Backend { Failure = Unavailable() };
        var fallback = new Backend();
        using var store = new SecretStore(_directory, native, fallback);
        native.Failure = null;
        await store.RetryNativeAsync(Ct);
        await store.SetAsync("k", "v", Ct);
        native.Values["k"].ShouldBe("v");
        fallback.Calls.ShouldBe(0);
        store.Info.IsOsKeychain.ShouldBeTrue();
    }

    [Theory]
    [InlineData("get")]
    [InlineData("exists")]
    [InlineData("set")]
    [InlineData("delete")]
    public async Task Authorized_fallback_preserves_unavailable_native_key_references(string operation)
    {
        using (var nativeStore = new SecretStore(_directory, new Backend(), new Backend()))
        {
            await nativeStore.SetAsync("native-key", "native value", Ct);
        }
        using var fallback = new EncryptedFileSecretStore(_directory);
        using var store = new SecretStore(_directory, null, fallback, new(SecretFallbackMode.Allow));
        _ = await Should.ThrowAsync<SecretBackendUnavailableException>(() => Run(store, operation));
        (await store.ListAsync(Ct)).ShouldContain("native-key");
        File.Exists(Path.Combine(_directory, "secrets.json")).ShouldBeFalse();
    }

    [Fact]
    public async Task Corrupt_reference_index_does_not_allow_overwrite_in_fallback()
    {
        _ = Directory.CreateDirectory(_directory);
        await File.WriteAllTextAsync(Path.Combine(_directory, "secret-keyrefs.json"), "broken", Ct);
        using var store = new SecretStore(_directory, null, new EncryptedFileSecretStore(_directory), new(SecretFallbackMode.Allow));
        _ = await Should.ThrowAsync<SecretBackendUnavailableException>(() => store.SetAsync("native-key", "v", Ct));
        File.Exists(Path.Combine(_directory, "secrets.json")).ShouldBeFalse();
    }

    [Fact]
    public async Task Disposal_drains_consent_persistence_and_rejects_new_control_operations()
    {
        var settings = new ControlledSettings { BlockSave = true };
        var fallback = new Backend();
        var store = new SecretStore(_directory, null, fallback, settings: settings);
        var admitted = store.EnableFallbackAsync(Ct);
        await settings.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5), Ct);
        store.Dispose();
        fallback.DisposeCount.ShouldBe(0);
        _ = await Should.ThrowAsync<ObjectDisposedException>(() => store.EnableFallbackAsync(Ct));
        _ = await Should.ThrowAsync<ObjectDisposedException>(() => store.RetryNativeAsync(Ct));
        settings.Release.SetResult();
        await admitted.WaitAsync(TimeSpan.FromSeconds(5), Ct);
        fallback.DisposeCount.ShouldBe(1);
        store.Dispose();
        fallback.DisposeCount.ShouldBe(1);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("broken")]
    [InlineData("{\"secretFallback\":\"ALLOW\"}")]
    [InlineData("{\"secretFallback\":1}")]
    [InlineData("{\"secretFallback\":\"block\",\"secretFallback\":\"allow\"}")]
    public async Task Invalid_machine_policy_fails_closed(string text)
    {
        _ = Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, "policy.json");
        await File.WriteAllTextAsync(path, text, Ct);
        var policy = SecretFallbackPolicy.Load(path);
        policy.Mode.ShouldBe(SecretFallbackMode.Block);
        _ = policy.Problem.ShouldNotBeNull();
    }

    [Theory]
    [InlineData("allow", SecretFallbackMode.Allow)]
    [InlineData("block", SecretFallbackMode.Block)]
    [InlineData("requireConsent", SecretFallbackMode.RequireConsent)]
    public async Task Machine_policy_reads_only_the_documented_explicit_values(string text, SecretFallbackMode expected)
    {
        _ = Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, "policy.json");
        await File.WriteAllTextAsync(path, "{\"secretFallback\":\"" + text + "\"}", Ct);
        SecretFallbackPolicy.Load(path).Mode.ShouldBe(expected);
        SecretFallbackPolicy.Load(Path.Combine(_directory, "absent")).Mode.ShouldBe(SecretFallbackMode.RequireConsent);
    }

    [Fact]
    public async Task Settings_consent_survives_a_real_json_round_trip()
    {
        var path = Path.Combine(_directory, "settings.json");
        var settings = new JsonSettingsStore(path);
        using var store = new SecretStore(_directory, null, new Backend(), settings: settings);
        await store.EnableFallbackAsync(Ct);
        var reloaded = await new JsonSettingsStore(path).LoadAsync(Ct);
        reloaded.Security.SecretFallback.ShouldBe(SecretFallbackMode.Allow);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Settings_requires_a_positive_confirmation_and_refreshes_the_display(bool confirm)
    {
        var settings = new FakeSettingsStore();
        using var store = new SecretStore(_directory, null, new EncryptedFileSecretStore(_directory), settings: settings);
        var dialogs = new FakeDialogService { ConfirmResult = confirm };
        using var vm = new SettingsDocumentViewModel(settings, new FakeThemeService(), dialogs, secrets: store,
            dispatcher: new ImmediateUiDispatcher());
        await vm.EnableSecretFallbackCommand.ExecuteAsync(null);
        dialogs.ConfirmCount.ShouldBe(1);
        settings.SaveCount.ShouldBe(confirm ? 1 : 0);
        vm.CanEnableSecretFallback.ShouldBe(!confirm);
        vm.SecretBackendName.ShouldContain(confirm ? "Encrypted file" : "Unavailable");
    }

    [Fact]
    public async Task Environment_save_stays_open_with_actionable_error_when_storage_is_blocked()
    {
        using var store = new SecretStore(_directory, null, new Backend(), new(SecretFallbackMode.Block));
        var vm = new EnvironmentEditorViewModel(store) { Name = "test" };
        vm.AddVariableCommand.Execute(null);
        vm.Variables[0].Name = "TOKEN";
        vm.Variables[0].IsSecret = true;
        vm.Variables[0].Value = "canary";
        await vm.SaveCommand.ExecuteAsync(null);
        _ = vm.SaveError.ShouldNotBeNull();
        vm.SaveError.ShouldContain("blocked");
        vm.SaveError.ShouldNotContain("canary");
        Directory.Exists(_directory).ShouldBeFalse();
    }

    [Theory]
    [InlineData(1u, 2, true)]
    [InlineData(1u, 1, false)]
    [InlineData(1u, 3, false)]
    [InlineData(2u, 2, true)]
    [InlineData(2u, 3, true)]
    [InlineData(2u, 4, true)]
    [InlineData(2u, 5, true)]
    [InlineData(2u, 6, true)]
    [InlineData(2u, 7, true)]
    [InlineData(2u, 9, true)]
    [InlineData(2u, 10, true)]
    [InlineData(2u, 11, true)]
    [InlineData(2u, 12, true)]
    [InlineData(2u, 15, true)]
    [InlineData(2u, 20, true)]
    [InlineData(2u, 23, true)]
    [InlineData(2u, 24, true)]
    [InlineData(2u, 25, true)]
    [InlineData(2u, 26, true)]
    [InlineData(2u, 27, true)]
    [InlineData(2u, 28, true)]
    [InlineData(2u, 29, true)]
    [InlineData(2u, 30, true)]
    [InlineData(2u, 31, true)]
    [InlineData(2u, 32, true)]
    [InlineData(2u, 33, true)]
    [InlineData(2u, 34, true)]
    [InlineData(2u, 0, false)]
    [InlineData(2u, 16, false)]
    [InlineData(2u, 19, false)]
    [InlineData(2u, 21, false)]
    [InlineData(2u, 22, false)]
    [InlineData(2u, 35, false)]
    [InlineData(2u, 36, false)]
    [InlineData(2u, 41, false)]
    [InlineData(2u, 99, false)]
    [InlineData(3u, 0, true)]
    [InlineData(3u, 1, true)]
    [InlineData(3u, 13, true)]
    [InlineData(3u, 14, true)]
    [InlineData(3u, 15, true)]
    [InlineData(3u, 18, true)]
    [InlineData(3u, 24, true)]
    [InlineData(3u, 39, true)]
    [InlineData(3u, 44, true)]
    [InlineData(3u, 45, true)]
    [InlineData(3u, 2, false)]
    [InlineData(3u, 19, false)]
    [InlineData(3u, 20, false)]
    [InlineData(3u, 34, false)]
    [InlineData(3u, 35, false)]
    [InlineData(3u, 36, false)]
    [InlineData(3u, 46, false)]
    [InlineData(3u, 99, false)]
    [InlineData(0u, 2, false)]
    [InlineData(4u, 0, false)]
    [InlineData(4u, 2, false)]
    [InlineData(4u, 25, false)]
    public void Linux_error_mapping_is_domain_and_code_specific(uint domain, int code, bool expected)
        => LibsecretErrorMapping.IsUnavailable(domain, code, 1, 2, 3).ShouldBe(expected);

    [Fact]
    public async Task Test_connection_reports_unavailable_pkcs12_secret_as_a_failure()
    {
        var profile = new TlsProfile { Name = "pfx", ClientCertPath = "client.pfx", ClientCertPasswordSecretRef = "password" };
        var workspace = new FakeWorkspaceStore(new WorkspaceModel { TlsProfiles = [profile] });
        using var secrets = new SecretStore(_directory, null, new Backend(), new(SecretFallbackMode.Block));
        var registry = new ConnectionRegistry(new TlsProfileResolver(workspace, secrets));
        var result = await registry.TestConnectionAsync(new SavedConnection
        {
            Name = "secure",
            Address = "localhost:1",
            Transport = TransportMode.Tls,
            TlsProfileId = profile.Id
        }, Ct);
        result.Ok.ShouldBeFalse();
        result.Message.ShouldContain("blocked");
        result.ServiceCount.ShouldBeNull();
    }

    [Fact]
    public async Task Settings_disposal_removes_its_backend_state_subscription()
    {
        using var store = new SecretStore(_directory, null, new Backend());
        var vm = new SettingsDocumentViewModel(new FakeSettingsStore(), new FakeThemeService(),
            new FakeDialogService(), secrets: store);
        var handlers = typeof(SecretStore).GetField("StateChanged", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        ((Delegate?)handlers.GetValue(store))!.GetInvocationList().Length.ShouldBe(1);
        vm.Dispose();
        handlers.GetValue(store).ShouldBeNull();
        await store.EnableFallbackAsync(Ct);
        handlers.GetValue(store).ShouldBeNull();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Closing_settings_cancels_pending_consent_without_enabling_fallback(bool dispose)
    {
        var settings = new ControlledSettings { BlockSave = true };
        using var store = new SecretStore(_directory, null, new Backend(), settings: settings);
        using var vm = new SettingsDocumentViewModel(settings, new FakeThemeService(),
            new FakeDialogService { ConfirmResult = true }, secrets: store);
        var consent = vm.EnableSecretFallbackCommand.ExecuteAsync(null);
        await settings.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5), Ct);
        if (dispose)
        {
            vm.Dispose();
        }
        await vm.CancelAndDrainAsync().WaitAsync(TimeSpan.FromSeconds(5), Ct);
        await consent;
        settings.Current.Security.SecretFallback.ShouldBe(SecretFallbackMode.RequireConsent);
        store.State.CanEnableFallback.ShouldBeTrue();
        Directory.Exists(_directory).ShouldBeFalse();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Blocked_secret_cleanup_preserves_the_workspace_and_tls_references(bool tls)
    {
        var environment = new WorkspaceEnvironment { Id = "env", Name = "env",
            Variables = [new EnvironmentVariable { Name = "TOKEN", Value = StringOrSecret.Secret("key") }] };
        var profile = new TlsProfile { Id = "tls", Name = "tls", ClientCertPasswordSecretRef = "key" };
        var connection = new SavedConnection { Name = "c", TlsProfileId = profile.Id };
        var workspace = new FakeWorkspaceStore(new WorkspaceModel
            { Environments = [environment], TlsProfiles = [profile], Connections = [connection] });
        using var secrets = new SecretStore(_directory, null, new Backend(), new(SecretFallbackMode.Block));
        _ = await Should.ThrowAsync<SecretBackendUnavailableException>(() => tls
            ? new TlsProfileStore(workspace, secrets).DeleteAsync(profile.Id, Ct)
            : new EnvironmentStore(workspace, secrets).DeleteAsync(environment.Id, Ct));
        workspace.SaveCount.ShouldBe(0);
        workspace.Current.Environments.ShouldContain(environment);
        workspace.Current.TlsProfiles.ShouldContain(profile);
        connection.TlsProfileId.ShouldBe(profile.Id);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Failed_workspace_save_does_not_destroy_the_referenced_secret(bool tls)
    {
        var environment = new WorkspaceEnvironment { Id = "env", Name = "env",
            Variables = [new EnvironmentVariable { Name = "TOKEN", Value = StringOrSecret.Secret("key") }] };
        var profile = new TlsProfile { Id = "tls", Name = "tls", ClientCertPasswordSecretRef = "key" };
        var workspace = new FakeWorkspaceStore(new WorkspaceModel { Environments = [environment], TlsProfiles = [profile] })
            { SaveError = new IOException("disk full") };
        using var secrets = new SecretStore(_directory, new Backend(), new Backend());
        await secrets.SetAsync("key", "retained", Ct);
        _ = await Should.ThrowAsync<IOException>(() => tls
            ? new TlsProfileStore(workspace, secrets).DeleteAsync(profile.Id, Ct)
            : new EnvironmentStore(workspace, secrets).DeleteAsync(environment.Id, Ct));
        (await secrets.GetAsync("key", Ct)).ShouldBe("retained");
    }

    private static SecretBackendUnavailableException Unavailable() => new("locked");

    private static Task Run(SecretStore store, string operation) => operation switch
    {
        "set" => store.SetAsync("native-key", "replacement", Ct),
        "get" => store.GetAsync("native-key", Ct),
        "exists" => store.ExistsAsync("native-key", Ct),
        "delete" => store.DeleteAsync("native-key", Ct),
        _ => throw new ArgumentException(operation)
    };

    private sealed class Backend : ISecretBackend, IDisposable
    {
        public Exception? Failure { get; set; }
        public int Calls { get; private set; }
        public int DisposeCount { get; private set; }
        public Dictionary<string, string> Values { get; } = [];
        public SecretStoreInfo Info { get; } = new("Native fake", true, null);
        private void Check()
        {
            Calls++;
            if (Failure is not null) { throw Failure; }
        }
        public Task SetAsync(string keyRef, string value, CancellationToken cancellationToken = default)
        {
            Check(); Values[keyRef] = value; return Task.CompletedTask;
        }
        public Task<string?> GetAsync(string keyRef, CancellationToken cancellationToken = default)
        {
            Check(); return Task.FromResult(Values.GetValueOrDefault(keyRef));
        }
        public Task<bool> ExistsAsync(string keyRef, CancellationToken cancellationToken = default)
        {
            Check(); return Task.FromResult(Values.ContainsKey(keyRef));
        }
        public Task DeleteAsync(string keyRef, CancellationToken cancellationToken = default)
        {
            Check(); _ = Values.Remove(keyRef); return Task.CompletedTask;
        }
        public void Dispose() => DisposeCount++;
    }

    private sealed class ControlledSettings : ISettingsStore
    {
        public bool FailSave { get; init; }
        public bool BlockSave { get; init; }
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public StudioSettings Current { get; private set; } = new();
        public event EventHandler? Changed;
        public Task<StudioSettings> LoadAsync(CancellationToken cancellationToken = default) => Task.FromResult(Current);
        public async Task SaveAsync(StudioSettings settings, CancellationToken cancellationToken = default)
        {
            _ = Entered.TrySetResult();
            if (FailSave) { throw new IOException("disk full"); }
            if (BlockSave) { await Release.Task.WaitAsync(cancellationToken); }
            Current = settings;
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }
}
