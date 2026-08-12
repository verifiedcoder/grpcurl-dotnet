using GrpCurl.Net.Studio.Services;
using GrpCurl.Net.Studio.Services.Secrets;
using GrpCurl.Net.Studio.TestSupport;
using GrpCurl.Net.Studio.ViewModels.Documents;
using GrpCurl.Net.Studio.ViewModels.Models.Connections;
using GrpCurl.Net.Studio.ViewModels.Services;
using Microsoft.Extensions.DependencyInjection;

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
        // The type is [SupportedOSPlatform("windows")]; construction is plain file-path work but the
        // analyser (and DPAPI on use) require the guard. Unreachable before PRD-005 in any case:
        // SecretStore owns it, and SecretStore's own Dispose threw first.
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

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
    public void Capture_writer_disposes_twice_without_throwing()
        => Should.NotThrow(() =>
        {
            var writer = new StreamCaptureWriter(new StringWriter(), _ => "{}");

            writer.Dispose();
            writer.Dispose();
        });

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

        // Pinned rather than incidental: the disposed gate is what rejects the call, and callers can
        // rely on ObjectDisposedException rather than a NullReferenceException from the zeroed key.
        _ = await Should.ThrowAsync<ObjectDisposedException>(async () => await store.SetAsync("studio/v1/test/key2", "v", Ct));
    }

    #endregion

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
