using GrpCurl.Net.Studio.Services;
using GrpCurl.Net.Studio.TestSupport;
using GrpCurl.Net.Studio.ViewModels;
using GrpCurl.Net.Studio.ViewModels.Documents;
using GrpCurl.Net.Studio.ViewModels.Models;
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

    // ── Round 4: ownership coverage and quiescence ───────────────────────────

    /// <summary>
    ///     PRD-005 re-review round 4, finding 1: a tab the user closed still owns whatever it started.
    ///     Disposal is not completion — the tab leaves <c>Documents</c>, but its refresh is still inside
    ///     the singleton secret store.
    /// </summary>
    [Fact]
    public async Task Work_from_a_closed_tab_still_counts_at_shutdown()
    {
        var service = new Blocker();
        var docs = Docs(secrets: new BlockingSecretStore(service));

        docs.OpenSettings();

        await service.Entered.Task.WaitAsync(Bounded, Ct);

        docs.Documents[0].CloseCommand.Execute(null);

        docs.Documents.ShouldBeEmpty("the tab is gone; the obligation to wait for its work is not");

        var result = await docs.DisposeOpenDocumentsAsync(ShortDrain).WaitAsync(Bounded, Ct);

        try
        {
            result.Drained.ShouldBeFalse("a closed tab's work is still work");
        }
        finally
        {
            service.Release.SetResult();
        }
    }

    /// <summary>The same for the bulk path, which takes a different route out of the collection.</summary>
    [Fact]
    public async Task Work_from_a_tab_closed_by_close_all_still_counts_at_shutdown()
    {
        var service = new Blocker();
        var docs = Docs(secrets: new BlockingSecretStore(service));

        docs.OpenSettings();

        await service.Entered.Task.WaitAsync(Bounded, Ct);

        docs.CloseAll();

        var result = await docs.DisposeOpenDocumentsAsync(ShortDrain).WaitAsync(Bounded, Ct);

        try
        {
            result.Drained.ShouldBeFalse("CloseAll must hand the work on too");
        }
        finally
        {
            service.Release.SetResult();
        }
    }

    /// <summary>
    ///     Finding 2: a settings edit starts a write to the singleton settings store and forgets it.
    ///     This is the plain <c>_ = _settings.SaveAsync(...)</c> the enrolment convention missed.
    /// </summary>
    [Fact]
    public async Task Settings_persistence_work_keeps_shutdown_from_reporting_drained()
    {
        var service = new Blocker();

        var docs = new DocumentsViewModel(
            new FakeDescriptorService(), new ImmediateUiDispatcher(), new FakeClipboardService(),
            new FakeInvocationRunner(), new FakeDialogService(), new FakeLauncherService(),
            new FakeRequestValidator(), new BlockingSettingsStore(service), new FakeThemeService());

        docs.OpenSettings();

        ((SettingsDocumentViewModel)docs.Documents[0]).HistoryMaxEntries = 4242;

        await service.Entered.Task.WaitAsync(Bounded, Ct);

        var result = await docs.DisposeOpenDocumentsAsync(ShortDrain).WaitAsync(Bounded, Ct);

        try
        {
            result.Drained.ShouldBeFalse("the settings save was still using the singleton store");
        }
        finally
        {
            service.Release.SetResult();
        }
    }

    /// <summary>
    ///     Finding 2: a tab is a view-model graph, not one object. A response metadata row's reveal
    ///     command awaits the singleton <c>IRevealGate</c>, and no reflection over the tab's own
    ///     properties can see it.
    /// </summary>
    [Fact]
    public async Task A_child_rows_command_keeps_shutdown_from_reporting_drained()
    {
        var service = new Blocker();
        var docs = Docs();

        docs.OpenInvocation(Conn(), "pkg.Svc/Go", "{}");

        var tab = (InvocationDocumentViewModel)docs.Documents[0];
        var row = new MetadataRowViewModel(
            new MetadataItem("authorization", "Bearer x", IsBinary: false), new BlockingRevealGate(service));

        tab.ResponseHeaders.Add(row);

        var revealing = row.ToggleRevealCommand.ExecuteAsync(null);

        await service.Entered.Task.WaitAsync(Bounded, Ct);

        var result = await docs.DisposeOpenDocumentsAsync(ShortDrain).WaitAsync(Bounded, Ct);

        try
        {
            result.Drained.ShouldBeFalse("a child row's command is work the tab owns");
        }
        finally
        {
            service.Release.SetResult();

            await revealing.WaitAsync(Bounded, Ct);
        }
    }

    /// <summary>
    ///     Finding 3, on the production path the review identified: work that is <em>already</em> in the
    ///     drain can start more work before it finishes, and a drain that waited on a snapshot would
    ///     miss it.
    ///     <para>
    ///         Switching the body format back to JSON starts the reinterpret warning, which parks in a
    ///         dialog. Accepting it clears <c>RequestJson</c> from inside that still-running task, and
    ///         the generated property callback schedules a validation — so the warning registers a
    ///         successor and only then completes. Shutdown must not finish on the warning alone.
    ///     </para>
    ///     <para>
    ///         The tab starts in protobuf-text mode on purpose: validation is skipped in that mode
    ///         (<c>RunValidationAsync</c> returns early), so the only validator call in the whole test
    ///         is the successor's, and there is nothing else for the drain to be waiting on.
    ///     </para>
    /// </summary>
    [Fact]
    public async Task Work_started_by_work_already_in_the_drain_is_waited_for()
    {
        var dialog = new Blocker();
        var validation = new Blocker();

        var docs = new DocumentsViewModel(
            new FakeDescriptorService(), new ImmediateUiDispatcher(), new FakeClipboardService(),
            new FakeInvocationRunner(), new BlockingDialogService(dialog), new FakeLauncherService(),
            new SecondCallBlocksValidator(validation), new InMemorySettingsStore(), new FakeThemeService());

        // Empty body: switching to text now raises no warning dialog, so the only one is the switch back.
        docs.OpenInvocation(Conn(), "pkg.Svc/Go", string.Empty);

        var tab = (InvocationDocumentViewModel)docs.Documents[0];

        tab.ValidationDebounce = TimeSpan.Zero;
        tab.BodyFormat = RequestBodyFormat.Text;
        tab.RequestJson = "{\"a\":1}";

        // Back to JSON with a non-empty body: validation runs once (returns at once) and the warning
        // parks in the dialog.
        tab.BodyFormat = RequestBodyFormat.Json;

        await dialog.Entered.Task.WaitAsync(Bounded, Ct);

        // The drain starts while the warning is parked and the successor does not exist yet.
        var shutdown = docs.DisposeOpenDocumentsAsync(Bounded);

        dialog.Release.SetResult();

        // Accepting clears the body, which schedules the validation the drain has never seen.
        await validation.Entered.Task.WaitAsync(Bounded, Ct);

        await Task.Delay(250, Ct);

        shutdown.IsCompleted.ShouldBeFalse("the successor validation is still running");

        validation.Release.SetResult();

        var result = await shutdown.WaitAsync(Bounded, Ct);

        result.Drained.ShouldBeTrue();
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

    /// <summary>Lets the first validation through and parks every later one.</summary>
    private sealed class SecondCallBlocksValidator(Blocker blocker) : IRequestValidator
    {
        private int _calls;

        public async Task<IReadOnlyList<ValidationProblem>> ValidateAsync(
            SavedConnection connection, string methodSymbol, string requestJson, bool allowUnknownFields,
            CancellationToken cancellationToken)
        {
            if (Interlocked.Increment(ref _calls) > 1)
            {
                await blocker.EnterAsync();
            }

            return [];
        }
    }

    private sealed class BlockingSettingsStore(Blocker blocker) : ISettingsStore
    {
        public StudioSettings Current { get; } = new();

        public event EventHandler? Changed;

        public Task<StudioSettings> LoadAsync(CancellationToken cancellationToken = default)
        {
            Changed?.Invoke(this, EventArgs.Empty);

            return Task.FromResult(Current);
        }

        public async Task SaveAsync(StudioSettings settings, CancellationToken cancellationToken = default)
            => await blocker.EnterAsync();
    }

    private sealed class BlockingRevealGate(Blocker blocker) : IRevealGate
    {
        public async Task<bool> ConfirmRevealAsync()
        {
            await blocker.EnterAsync();

            return true;
        }
    }

    /// <summary>Parks in the confirmation, then answers "yes" — which is what starts the successor.</summary>
    private sealed class BlockingDialogService(Blocker blocker) : IDialogService
    {
        public Task ShowMessageAsync(string title, string message, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public async Task<bool> ConfirmAsync(string title, string message, CancellationToken cancellationToken = default)
        {
            await blocker.EnterAsync();

            return true;
        }

        public Task<TResult?> ShowDialogAsync<TResult>(DialogViewModel<TResult> dialogViewModel)
            => Task.FromResult<TResult?>(default);
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
