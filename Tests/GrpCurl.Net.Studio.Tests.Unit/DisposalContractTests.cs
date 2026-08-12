using GrpCurl.Net.Studio.Services;
using GrpCurl.Net.Studio.Services.Secrets;
using GrpCurl.Net.Studio.TestSupport;
using GrpCurl.Net.Studio.ViewModels.Documents;
using GrpCurl.Net.Studio.ViewModels.Models.Connections;
using GrpCurl.Net.Studio.ViewModels.Services;
using Microsoft.Extensions.DependencyInjection;
using System.Reflection;

namespace GrpCurl.Net.Studio.Tests.Unit;

/// <summary>
///     PRD-005: every Studio <see cref="IDisposable" /> must dispose idempotently and without throwing.
///     <para>
///         The three services here are container-owned singletons, so before this fix the first of them
///         to be disposed threw <see cref="NotImplementedException" /> out of
///         <c>ServiceProvider.Dispose()</c> — aborting disposal of every singleton after it and escaping
///         <c>Program.Main</c>, which is the one shutdown step with no exception handler. Every clean
///         Studio shutdown ended as an unhandled crash rather than the intended <c>Environment.Exit(0)</c>.
///     </para>
///     <para>
///         Scope of the container test below: it proves these types survive container disposal, not that
///         the production <c>ConfigureStudioServices</c> graph does. That graph hard-wires
///         <c>SecretStore</c> to <c>SpecialFolder.ApplicationData</c> with no seam and needs an Avalonia
///         dispatcher, so building it here would write to the developer's real profile.
///     </para>
/// </summary>
public sealed class DisposalContractTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "grpcn-dispose-" + Guid.NewGuid().ToString("N"));

    public DisposalContractTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, recursive: true);
        }
    }

    private static readonly TimeSpan Bounded = TimeSpan.FromSeconds(10);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    #region Double dispose — every type named by PRD-005, plus the view models the close flow now reaches

    [Fact]
    public void Secret_store_disposes_twice_without_throwing()
        => Should.NotThrow(() =>
        {
            var store = new SecretStore(_dir);

            store.Dispose();
            store.Dispose();
        });

    [Fact]
    public void Encrypted_file_secret_store_disposes_twice_without_throwing()
        => Should.NotThrow(() =>
        {
            var store = new EncryptedFileSecretStore(_dir);

            store.Dispose();
            store.Dispose();
        });

    [Fact]
    public void History_store_disposes_twice_without_throwing()
        => Should.NotThrow(() =>
        {
            var store = new JsonHistoryStore(Path.Combine(_dir, "history.ndjson"));

            store.Dispose();
            store.Dispose();
        });

    [Fact]
    public void Request_validator_disposes_twice_without_throwing()
        => Should.NotThrow(() =>
        {
            var validator = new RequestValidator(new InvocationService());

            validator.Dispose();
            validator.Dispose();
        });

    [Fact]
    public void Windows_dpapi_secret_store_disposes_twice_without_throwing()
    {
        // Reported as skipped rather than returning normally, so a run on Linux/macOS does not read as
        // if this were covered — the review caught the earlier version claiming "0 skipped".
        Assert.SkipWhen(!OperatingSystem.IsWindows(), "Windows DPAPI only.");

        Should.NotThrow(() =>
        {
            var store = new WindowsDpapiSecretStore(_dir);

            store.Dispose();
            store.Dispose();
        });
    }

    [Fact]
    public void Invocation_document_disposes_twice_without_throwing()
        => Should.NotThrow(() =>
        {
            var tab = CreateInvocationTab(out _);

            tab.Dispose();
            tab.Dispose();
        });

    [Fact]
    public void Graphql_document_disposes_twice_without_throwing()
        => Should.NotThrow(() =>
        {
            var tab = CreateGraphQlTab();

            tab.Dispose();
            tab.Dispose();
        });

    [Fact]
    public void Describe_document_disposes_twice_without_throwing()
        => Should.NotThrow(() =>
        {
            var tab = new DescribeDocumentViewModel(
                new SavedConnection { Name = "c", Address = "h:1" }, "pkg.Alpha",
                new FakeDescriptorService(), new ImmediateUiDispatcher(), new FakeClipboardService(),
                new FakeDocumentHost());

            tab.Dispose();
            tab.Dispose();
        });

    [Fact]
    public void Settings_document_disposes_twice_without_throwing()
        => Should.NotThrow(() =>
        {
            var tab = new SettingsDocumentViewModel(
                new InMemorySettingsStore(), new FakeThemeService(), new FakeDialogService());

            tab.Dispose();
            tab.Dispose();
        });

    [Fact]
    public void Capture_writer_disposes_twice_without_throwing()
        => Should.NotThrow(() =>
        {
            var writer = new StreamCaptureWriter(new StringWriter(), _ => "{}");

            writer.Dispose();
            writer.Dispose();
        });

    /// <summary>
    ///     Shutdown and the close flow can both reach a tab's <c>Dispose</c>, so the guard has to be
    ///     atomic. A plain Boolean read/write lets two threads both pass it and run the body twice.
    ///     <para>
    ///         A smoke test, and described as one: it makes the interleaving likely, not certain, so a
    ///         pass is not proof of atomicity. The guarantee comes from the <see cref="Interlocked" />
    ///         guard in each <c>Dispose</c>; this exists so that a regression to a plain Boolean has
    ///         something that can catch it.
    ///     </para>
    /// </summary>
    [Fact]
    public async Task Concurrent_disposal_of_a_tab_does_not_throw()
    {
        for (var attempt = 0; attempt < 50; attempt++)
        {
            var tab = CreateInvocationTab(out _);
            var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

            var racers = Enumerable.Range(0, 4).Select(_ => Task.Run(async () =>
            {
                await start.Task;

                tab.Dispose();
            }, Ct)).ToArray();

            start.SetResult();

            await Should.NotThrowAsync(async () => await Task.WhenAll(racers).WaitAsync(Bounded, Ct));
        }
    }

    #endregion

    #region The shutdown crash itself

    [Fact]
    public void Disposing_a_container_holding_the_studio_singletons_does_not_throw()
    {
        var services = new ServiceCollection();

        // Registered the way ServiceRegistration registers them — as singletons the container creates
        // and therefore tracks for disposal — but pointed at a temp directory instead of ApplicationData.
        _ = services.AddSingleton<ISecretStore>(_ => new SecretStore(_dir));
        _ = services.AddSingleton<IHistoryStore>(_ => new JsonHistoryStore(Path.Combine(_dir, "history.ndjson")));
        _ = services.AddSingleton<IRequestValidator>(_ => new RequestValidator(new InvocationService()));

        var provider = services.BuildServiceProvider();

        // Resolve all three first: the container only disposes singletons it actually created, so an
        // unresolved registration would make this pass for the wrong reason.
        _ = provider.GetRequiredService<ISecretStore>();
        _ = provider.GetRequiredService<IHistoryStore>();
        _ = provider.GetRequiredService<IRequestValidator>();

        Should.NotThrow(provider.Dispose);
    }

    #endregion

    #region Key material

    [Fact]
    public async Task Disposing_the_fallback_store_zeroes_the_derived_key()
    {
        var store = new EncryptedFileSecretStore(_dir);

        // Forces HKDF derivation — the key is cached lazily on first use, so a store that never
        // encrypted anything would have nothing to zero and would pass vacuously.
        await store.SetAsync("studio/v1/test/key", "value", Ct);

        var key = store.KeyForTests;

        _ = key.ShouldNotBeNull();
        key.ShouldContain(b => b != 0, "the derived key must be non-zero before disposal");

        store.Dispose();

        // The captured buffer, not the field: proves Dispose cleared the bytes rather than only
        // dropping the reference and leaving the key readable in the heap.
        key.ShouldAllBe(b => b == 0);
        store.KeyForTests.ShouldBeNull();
    }

    [Fact]
    public async Task Using_a_disposed_store_fails_deterministically()
    {
        var store = new EncryptedFileSecretStore(_dir);

        await store.SetAsync("studio/v1/test/key", "value", Ct);

        store.Dispose();

        // Pinned rather than incidental: the entry-point check is what rejects the call, so callers get
        // ObjectDisposedException rather than a NullReferenceException from the zeroed key.
        _ = await Should.ThrowAsync<ObjectDisposedException>(async () => await store.SetAsync("studio/v1/test/key2", "v", Ct));
    }

    /// <summary>
    ///     PRD-005 re-review, finding 2: the key belongs to whoever owns the gate. Disposal arriving
    ///     while an operation is inside the critical section must leave the key alone and let that
    ///     operation destroy it on its way out.
    ///     <para>
    ///         The previous implementation waited two seconds for the gate and then zeroed the key
    ///         regardless, so an operation mid-AES could have its key cleared underneath it. Both
    ///         assertions below fail against that version — the first because the key is already gone,
    ///         the second because the admitted write faults instead of completing.
    ///     </para>
    /// </summary>
    [Fact]
    public async Task Disposal_under_an_owner_leaves_the_key_to_the_owner()
    {
        var store = new EncryptedFileSecretStore(_dir);

        await store.SetAsync("studio/v1/test/key", "value", Ct);

        var key = store.KeyForTests.ShouldNotBeNull();

        // Models an operation inside the critical section. Holding the real gate is what makes this a
        // test of ownership rather than of timing.
        await store.GateForTests.WaitAsync(Ct);

        // Admitted before disposal: SetAsync runs synchronously as far as its first await — the gate
        // wait — so this call is past the disposed check and queued behind the owner above.
        var admitted = store.SetAsync("studio/v1/test/key2", "value2", Ct);

        store.Dispose();

        key.ShouldContain(b => b != 0, "key material must survive while the gate is owned");
        _ = store.KeyForTests.ShouldNotBeNull();

        _ = store.GateForTests.Release();

        // Drained, not aborted: work admitted before disposal finishes with a valid key.
        await Should.NotThrowAsync(async () => await admitted.WaitAsync(Bounded, Ct));

        // ...and the last one out still destroys it.
        key.ShouldAllBe(b => b == 0);
        store.KeyForTests.ShouldBeNull();
    }

    #endregion

    #region The router's own lifetime (PRD-005 re-review, finding 3)

    [Fact]
    public async Task Every_router_operation_is_rejected_after_disposal()
    {
        var store = new SecretStore(_dir);

        // A backend that is neither disposable nor disposed-aware — the shape of the macOS Keychain and
        // Linux Secret Service paths. Without a check of its own on the facade, reads and existence
        // probes reached straight through it and were served after Dispose() had returned.
        SubstituteBackend(store, new EchoBackend());

        store.Dispose();

        // ObjectName pins *which* object refused. Asserting only the exception type would pass on the
        // fallback path for the wrong reason: the encrypted file store rejects post-disposal calls too,
        // and that says nothing about the router.
        (await Rejected(() => store.GetAsync("k", Ct))).ShouldBe(typeof(SecretStore).FullName);
        (await Rejected(() => store.ExistsAsync("k", Ct))).ShouldBe(typeof(SecretStore).FullName);
        (await Rejected(() => store.SetAsync("k", "v", Ct))).ShouldBe(typeof(SecretStore).FullName);
        (await Rejected(() => store.DeleteAsync("k", Ct))).ShouldBe(typeof(SecretStore).FullName);
        (await Rejected(() => store.ListAsync(Ct))).ShouldBe(typeof(SecretStore).FullName);

        static async Task<string?> Rejected(Func<Task> operation)
            => (await Should.ThrowAsync<ObjectDisposedException>(operation)).ObjectName;
    }

    /// <summary>
    ///     An operation the router admitted must finish against a live backend. A public set spans a
    ///     backend write and a later index update, so disposing the backends on the disposed check alone
    ///     could tear one down mid-operation.
    /// </summary>
    [Fact]
    public async Task Disposal_waits_for_an_admitted_operation_before_releasing_the_backends()
    {
        var store = new SecretStore(_dir);
        var backend = new BlockingBackend();

        SubstituteBackend(store, backend);

        var admitted = store.SetAsync("studio/v1/test/key", "value", Ct);

        await backend.Entered.Task.WaitAsync(Bounded, Ct);

        store.Dispose();

        backend.DisposeCount.ShouldBe(0, "the backend is still serving an admitted operation");

        backend.Release.SetResult();

        await admitted.WaitAsync(Bounded, Ct);

        // The last operation out is what releases them — and exactly once.
        backend.DisposeCount.ShouldBe(1);

        store.Dispose();

        backend.DisposeCount.ShouldBe(1);
    }

    /// <summary>
    ///     Points the router at a test backend. Reflection rather than a constructor seam: the seam
    ///     would be a second, untested way to build the store, and the probe-and-commit logic in the
    ///     real constructor is exactly what a test of disposal should keep.
    /// </summary>
    private static void SubstituteBackend(SecretStore store, ISecretBackend backend)
    {
        Set("_active", backend);
        Set("_fallback", backend);
        Set("_native", null);
        Set("_activeIsNative", false);

        void Set(string field, object? value)
            => typeof(SecretStore)
                .GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)!
                .SetValue(store, value);
    }

    private sealed class EchoBackend : ISecretBackend
    {
        public SecretStoreInfo Info { get; } = new("Test (not disposable)", IsOsKeychain: true, null);

        public Task SetAsync(string keyRef, string value, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task<string?> GetAsync(string keyRef, CancellationToken cancellationToken = default)
            => Task.FromResult<string?>("served");

        public Task DeleteAsync(string keyRef, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task<bool> ExistsAsync(string keyRef, CancellationToken cancellationToken = default)
            => Task.FromResult(true);
    }

    private sealed class BlockingBackend : ISecretBackend, IDisposable
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int DisposeCount { get; private set; }

        public SecretStoreInfo Info { get; } = new("Test (blocking)", IsOsKeychain: true, null);

        public async Task SetAsync(string keyRef, string value, CancellationToken cancellationToken = default)
        {
            _ = Entered.TrySetResult();

            await Release.Task;
        }

        public Task<string?> GetAsync(string keyRef, CancellationToken cancellationToken = default)
            => Task.FromResult<string?>(null);

        public Task DeleteAsync(string keyRef, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task<bool> ExistsAsync(string keyRef, CancellationToken cancellationToken = default)
            => Task.FromResult(false);

        public void Dispose() => DisposeCount++;
    }

    #endregion

    private static GraphQlDocumentViewModel CreateGraphQlTab()
        => new(
            new SavedConnection { Name = "c", Address = "h:1" },
            new FakeGraphQlService(), new ImmediateUiDispatcher(), new FakeClipboardService());

    private static InvocationDocumentViewModel CreateInvocationTab(out EnvironmentService environment)
    {
        var workspace = new FakeWorkspaceStore(new WorkspaceModel
        {
            Environments =
            [
                new WorkspaceEnvironment
                {
                    Id = "e1", Name = "staging",
                    Variables = [new EnvironmentVariable { Name = "HOST", Value = StringOrSecret.Plain("api:443") }]
                }
            ]
        });

        environment = new EnvironmentService(workspace, new FakeSecretStore());

        return new InvocationDocumentViewModel(
            new SavedConnection { Name = "c", Address = "h:1" }, "pkg.Svc/Go", "{}",
            new FakeInvocationRunner(), new FakeDescriptorService(), new ImmediateUiDispatcher(),
            new FakeClipboardService(), new FakeDialogService(), new FakeLauncherService(),
            new FakeRequestValidator(), environment: environment);
    }
}
