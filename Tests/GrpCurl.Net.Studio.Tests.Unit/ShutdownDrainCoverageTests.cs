using GrpCurl.Net.Studio.Services;
using GrpCurl.Net.Studio.TestSupport;
using GrpCurl.Net.Studio.ViewModels.Documents;
using GrpCurl.Net.Studio.ViewModels.Models.Connections;
using GrpCurl.Net.Studio.ViewModels.Models.Descriptors;
using GrpCurl.Net.Studio.ViewModels.Models.History;
using GrpCurl.Net.Studio.ViewModels.Models.Invocation;
using GrpCurl.Net.Studio.ViewModels.Services;

namespace GrpCurl.Net.Studio.Tests.Unit;

/// <summary>
///     PRD-005 re-review round 3, finding 1: <c>Drained</c> must describe <b>all</b> the work the open
///     tabs own, not the subset that happens to be cancellable.
///     <para>
///         Round 2 drained the toolkit-generated commands of three tab types. Everything else a tab
///         starts was invisible to it — debounced validation, GraphQL debounce work, superseded describe
///         loads, and the whole of Settings and History — so shutdown could report success and dispose
///         the container while one of those was still calling into a singleton it owns. Each case here
///         blocks a real service, and each fails against that version.
///     </para>
///     <para>
///         The bound is deliberately short (200ms) where the assertion is "shutdown noticed": a test
///         that waited the production five seconds per case would add half a minute to the suite for no
///         extra evidence. <see cref="Shutdown_waits_for_uncancellable_settings_work" /> is the one that
///         proves waiting rather than timing out.
///     </para>
/// </summary>
public sealed class ShutdownDrainCoverageTests
{
    private static readonly TimeSpan Bounded = TimeSpan.FromSeconds(10);

    private static readonly TimeSpan ShortDrain = TimeSpan.FromMilliseconds(200);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static SavedConnection Conn() => new() { Name = "c", Address = "h:1" };

    private static DocumentsViewModel Docs(
        IDescriptorService? descriptors = null,
        IRequestValidator? validator = null,
        ISecretStore? secrets = null,
        IHistoryStore? history = null)
        => new(
            descriptors ?? new FakeDescriptorService(), new ImmediateUiDispatcher(), new FakeClipboardService(),
            new FakeInvocationRunner(), new FakeDialogService(), new FakeLauncherService(),
            validator ?? new FakeRequestValidator(), new InMemorySettingsStore(), new FakeThemeService(),
            history: history, secrets: secrets, workspace: new FakeWorkspaceStore(new WorkspaceModel()));

    [Fact]
    public async Task Debounced_validation_work_keeps_shutdown_from_reporting_drained()
    {
        var service = new Blocker();
        var docs = Docs(validator: new BlockingValidator(service));

        docs.OpenInvocation(Conn(), "pkg.Svc/Go", "{}");

        var tab = (InvocationDocumentViewModel)docs.Documents[0];

        // The validation task is started by the property change, not by a command — which is exactly
        // why the command-only drain never saw it.
        tab.ValidationDebounce = TimeSpan.Zero;
        tab.RequestJson = "{\"a\":1}";

        await service.Entered.Task.WaitAsync(Bounded, Ct);

        var result = await docs.DisposeOpenDocumentsAsync(ShortDrain).WaitAsync(Bounded, Ct);

        try
        {
            result.Drained.ShouldBeFalse("validation was still running against the singleton validator");
        }
        finally
        {
            service.Release.SetResult();
        }
    }

    [Fact]
    public async Task A_superseded_describe_load_keeps_shutdown_from_reporting_drained()
    {
        var service = new Blocker();

        var descriptors = new FakeDescriptorService
        {
            OnDescribe = async (_, symbol, _) =>
            {
                if (symbol == "pkg.First")
                {
                    await service.EnterAsync();
                }

                return DescribeResult.Failure(new DescriptorLoadError("stub", null, false));
            }
        };

        var docs = Docs(descriptors: descriptors);

        docs.OpenDescribe(Conn(), "pkg.First");

        var tab = (DescribeDocumentViewModel)docs.Documents[0];

        await service.Entered.Task.WaitAsync(Bounded, Ct);

        // Navigating cancels the first load but does not wait for it. Holding only the newest task made
        // the still-running one disappear from the drain.
        tab.NavigateCommand.Execute(new TypeRef("pkg.Second", Resolvable: true));

        var result = await docs.DisposeOpenDocumentsAsync(ShortDrain).WaitAsync(Bounded, Ct);

        try
        {
            result.Drained.ShouldBeFalse("the superseded load was still running");
        }
        finally
        {
            service.Release.SetResult();
        }
    }

    [Fact]
    public async Task Settings_constructor_work_keeps_shutdown_from_reporting_drained()
    {
        var service = new Blocker();
        var docs = Docs(secrets: new BlockingSecretStore(service));

        docs.OpenSettings();

        await service.Entered.Task.WaitAsync(Bounded, Ct);

        var result = await docs.DisposeOpenDocumentsAsync(ShortDrain).WaitAsync(Bounded, Ct);

        try
        {
            result.Drained.ShouldBeFalse("the Settings refresh was still using the singleton secret store");
        }
        finally
        {
            service.Release.SetResult();
        }
    }

    [Fact]
    public async Task History_load_work_keeps_shutdown_from_reporting_drained()
    {
        var service = new Blocker();
        var docs = Docs(history: new BlockingHistoryStore(service));

        docs.OpenHistory();

        await service.Entered.Task.WaitAsync(Bounded, Ct);

        var result = await docs.DisposeOpenDocumentsAsync(ShortDrain).WaitAsync(Bounded, Ct);

        try
        {
            result.Drained.ShouldBeFalse("the History load was still using the singleton history store");
        }
        finally
        {
            service.Release.SetResult();
        }
    }

    /// <summary>
    ///     The other half of the contract: uncancellable work is <em>waited for</em>, not merely counted
    ///     as a timeout. Settings work has no token to cancel, so this is the case that would have been
    ///     lost had the "do not drain what cannot be cancelled" decision stood.
    /// </summary>
    [Fact]
    public async Task Shutdown_waits_for_uncancellable_settings_work()
    {
        var service = new Blocker();
        var docs = Docs(secrets: new BlockingSecretStore(service));

        docs.OpenSettings();

        await service.Entered.Task.WaitAsync(Bounded, Ct);

        var shutdown = docs.DisposeOpenDocumentsAsync(Bounded);

        // The refresh is still parked, so a shutdown that skipped it would already be finished. The
        // delay only gives that version time to be wrong.
        await Task.Delay(250, Ct);

        shutdown.IsCompleted.ShouldBeFalse("shutdown must wait for work it cannot cancel");

        service.Release.SetResult();

        var result = await shutdown.WaitAsync(Bounded, Ct);

        result.Drained.ShouldBeTrue("the refresh finished well inside the timeout");
    }

    /// <summary>A service that parks its caller until the test lets go, and says when it was reached.</summary>
    private sealed class Blocker
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task EnterAsync()
        {
            _ = Entered.TrySetResult();

            await Release.Task;
        }
    }

    private sealed class BlockingValidator(Blocker blocker) : IRequestValidator
    {
        public async Task<IReadOnlyList<ValidationProblem>> ValidateAsync(
            SavedConnection connection, string methodSymbol, string requestJson, bool allowUnknownFields,
            CancellationToken cancellationToken)
        {
            await blocker.EnterAsync();

            return [];
        }
    }

    private sealed class BlockingSecretStore(Blocker blocker) : ISecretStore
    {
        public SecretStoreInfo Info { get; } = new("Test", IsOsKeychain: false, null);

        public Task SetAsync(string keyRef, string value, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task<string?> GetAsync(string keyRef, CancellationToken cancellationToken = default)
            => Task.FromResult<string?>(null);

        public Task DeleteAsync(string keyRef, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task<bool> ExistsAsync(string keyRef, CancellationToken cancellationToken = default)
            => Task.FromResult(false);

        public async Task<IReadOnlyList<string>> ListAsync(CancellationToken cancellationToken = default)
        {
            await blocker.EnterAsync();

            return [];
        }
    }

    private sealed class BlockingHistoryStore(Blocker blocker) : IHistoryStore
    {
        public Task AppendAsync(HistoryEntry entry, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public async Task<IReadOnlyList<HistoryEntry>> ReadAllAsync(CancellationToken cancellationToken = default)
        {
            await blocker.EnterAsync();

            return [];
        }

        public Task DeleteAsync(IReadOnlyCollection<string> ids, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task SetPinnedAsync(string id, bool pinned, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task ClearAsync(bool keepPinned, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task ExportAsync(string path, IReadOnlyList<HistoryEntry> entries, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }
}
