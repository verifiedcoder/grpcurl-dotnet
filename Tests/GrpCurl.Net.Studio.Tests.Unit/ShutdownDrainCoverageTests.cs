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

    // ── Round 5: participant discovery and ownership across removal ─────────

    /// <summary>
    ///     PRD-005 re-review rounds 5 and 6, finding 1: a tab opened by work the drain is already
    ///     waiting on must never be left running.
    ///     <para>
    ///         Round 5 grew the participant list, which covered it only when a round <em>completed</em>;
    ///         a round that ended at the timeout ran no further discovery, so the tab was neither
    ///         cancelled nor disposed. Admission now closes instead: from the moment shutdown starts, a
    ///         new tab is retired on the spot rather than joining <c>Documents</c>.
    ///     </para>
    ///     <para>
    ///         The probe stays blocked through the timeout, which is the case round 6 found. Disposal is
    ///         observed through the environment singleton's subscriber count — a tab subscribes when it
    ///         opens and unsubscribes only in <c>Dispose</c>, so the count returning to its baseline is
    ///         the disposal itself, not a proxy for it.
    ///     </para>
    /// </summary>
    [Fact]
    public async Task A_tab_opened_by_admitted_work_is_cancelled_and_disposed_even_on_timeout()
    {
        var source = new Blocker();
        var probe = new Blocker();

        DocumentsViewModel? docs = null;

        var environment = new EnvironmentService(
            new FakeWorkspaceStore(new WorkspaceModel
            {
                Environments = [new WorkspaceEnvironment { Id = "e1", Name = "staging" }]
            }),
            new FakeSecretStore());

        var descriptors = new FakeDescriptorService
        {
            OnDescribe = async (_, symbol, _) =>
            {
                if (symbol == "pkg.Source")
                {
                    // Parked when the drain starts, so this load is admitted work; it then opens a tab.
                    await source.EnterAsync();

                    docs!.OpenInvocation(Conn(), "pkg.Svc/Probe", "{}");
                }
                else
                {
                    // The probe's own method resolution ignores its token and stays blocked past the
                    // timeout — the case round 6 found, where no further discovery pass ever runs.
                    await probe.EnterAsync();
                }

                return DescribeResult.Failure(new DescriptorLoadError("stub", null, false));
            }
        };

        docs = new DocumentsViewModel(
            descriptors, new ImmediateUiDispatcher(), new FakeClipboardService(), new FakeInvocationRunner(),
            new FakeDialogService(), new FakeLauncherService(), new FakeRequestValidator(),
            new InMemorySettingsStore(), new FakeThemeService(), environment: environment);

        docs.OpenDescribe(Conn(), "pkg.Source");

        var baseline = environment.ActiveChangedSubscribers;

        await source.Entered.Task.WaitAsync(Bounded, Ct);

        var shutdown = docs.DisposeOpenDocumentsAsync(ShortDrain);

        source.Release.SetResult();

        var result = await shutdown.WaitAsync(Bounded, Ct);

        try
        {
            // The probe's own method resolution is still blocked, so the drain genuinely timed out.
            await probe.Entered.Task.WaitAsync(Bounded, Ct);

            result.Drained.ShouldBeFalse("the probe tab's work was still running");

            docs.Documents.ShouldNotContain(d => d is InvocationDocumentViewModel,
                "admission is closed once shutdown starts");

            environment.ActiveChangedSubscribers.ShouldBe(baseline,
                "the probe tab must have been disposed, not merely refused");
        }
        finally
        {
            probe.Release.SetResult();
        }
    }

    /// <summary>
    ///     Round 6, finding 2: the composer is listed as a collected child, but nothing pinned the link.
    ///     Its debounced validation runs against the same singleton validator the tab uses.
    /// </summary>
    [Fact]
    public async Task The_composers_validation_keeps_shutdown_from_reporting_drained()
    {
        var service = new Blocker();

        var descriptors = new FakeDescriptorService
        {
            OnDescribe = (_, symbol, _) => Task.FromResult(DescribeResult.Success(
                new MethodDescription(symbol, "Go", "f.proto", StreamingShape.ClientStreaming,
                    new TypeRef("pkg.In", true), new TypeRef("pkg.Out", true), new TypeRef("pkg.Svc", true), "{}")))
        };

        var docs = Docs(descriptors: descriptors, validator: new BlockingValidator(service));

        docs.OpenInvocation(Conn(), "pkg.Svc/Go", "{}");

        var tab = (InvocationDocumentViewModel)docs.Documents[0];
        var composer = tab.Composer.ShouldNotBeNull("a client-streaming method must have a composer");

        composer.ValidationDebounce = TimeSpan.Zero;
        composer.MessageJson = "{\"a\":1}";

        await service.Entered.Task.WaitAsync(Bounded, Ct);

        var result = await docs.DisposeOpenDocumentsAsync(ShortDrain).WaitAsync(Bounded, Ct);

        try
        {
            result.Drained.ShouldBeFalse("the composer's validation is work the tab owns");
        }
        finally
        {
            service.Release.SetResult();
        }
    }

    /// <summary>
    ///     Round 6, finding 2: the response-metadata hand-off was implemented but unpinned — the earlier
    ///     metadata case leaves its row in the collection, so it never exercises removal. Starting a new
    ///     invocation clears both metadata collections, which is the production path that drops the row.
    /// </summary>
    [Fact]
    public async Task A_reveal_command_survives_the_metadata_clear_that_removes_its_row()
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

        // Invoking clears ResponseHeaders/ResponseTrailers — the row leaves while its command runs.
        await tab.InvokeCommand.ExecuteAsync(null);

        tab.ResponseHeaders.ShouldNotContain(row);

        var result = await docs.DisposeOpenDocumentsAsync(ShortDrain).WaitAsync(Bounded, Ct);

        try
        {
            result.Drained.ShouldBeFalse("the cleared row's command is still running");
        }
        finally
        {
            service.Release.SetResult();

            await revealing.WaitAsync(Bounded, Ct);
        }
    }

    /// <summary>Round 6, finding 2: the same for <c>Log.Reset()</c>, the other stream-log removal path.</summary>
    [Fact]
    public async Task A_stream_rows_command_survives_a_log_reset()
    {
        var service = new Blocker();
        var clipboard = new BlockingClipboard(service);

        var docs = new DocumentsViewModel(
            new FakeDescriptorService(), new ImmediateUiDispatcher(), clipboard, new FakeInvocationRunner(),
            new FakeDialogService(), new FakeLauncherService(), new FakeRequestValidator(),
            new InMemorySettingsStore(), new FakeThemeService());

        var tab = new InvocationDocumentViewModel(
            Conn(), "pkg.Svc/Go", "{}", new FakeInvocationRunner(), new FakeDescriptorService(),
            new ImmediateUiDispatcher(), clipboard, new FakeDialogService(), new FakeLauncherService(),
            new FakeRequestValidator());

        docs.Documents.Add(tab);

        tab.Log.Append(Event(0));

        var copying = tab.Log.Rows[0].CopyAsNdjsonCommand.ExecuteAsync(null);

        await service.Entered.Task.WaitAsync(Bounded, Ct);

        tab.Log.Reset();

        tab.Log.Rows.ShouldBeEmpty();

        var result = await docs.DisposeOpenDocumentsAsync(ShortDrain).WaitAsync(Bounded, Ct);

        try
        {
            result.Drained.ShouldBeFalse("the reset row's command is still running");
        }
        finally
        {
            service.Release.SetResult();

            await copying.WaitAsync(Bounded, Ct);
        }
    }

    /// <summary>
    ///     The simpler half of the same wiring: a row still in the collection. Together with the
    ///     eviction case this is what pins <c>WorkGraph.Collect(Log, …)</c> — the ownership tripwire's
    ///     name list cannot, since it only checks that a type is accounted for, not that a parent
    ///     collects it.
    /// </summary>
    [Fact]
    public async Task A_live_stream_rows_command_keeps_shutdown_from_reporting_drained()
    {
        var service = new Blocker();
        var clipboard = new BlockingClipboard(service);

        var docs = new DocumentsViewModel(
            new FakeDescriptorService(), new ImmediateUiDispatcher(), clipboard, new FakeInvocationRunner(),
            new FakeDialogService(), new FakeLauncherService(), new FakeRequestValidator(),
            new InMemorySettingsStore(), new FakeThemeService());

        var tab = new InvocationDocumentViewModel(
            Conn(), "pkg.Svc/Go", "{}", new FakeInvocationRunner(), new FakeDescriptorService(),
            new ImmediateUiDispatcher(), clipboard, new FakeDialogService(), new FakeLauncherService(),
            new FakeRequestValidator());

        docs.Documents.Add(tab);

        tab.Log.Append(Event(0));

        var copying = tab.Log.Rows[0].CopyAsNdjsonCommand.ExecuteAsync(null);

        await service.Entered.Task.WaitAsync(Bounded, Ct);

        var result = await docs.DisposeOpenDocumentsAsync(ShortDrain).WaitAsync(Bounded, Ct);

        try
        {
            result.Drained.ShouldBeFalse("a stream row's command is work the tab owns");
        }
        finally
        {
            service.Release.SetResult();

            await copying.WaitAsync(Bounded, Ct);
        }
    }

    /// <summary>
    ///     PRD-005 re-review round 5, finding 2: current reachability is not durable ownership. The
    ///     stream log is a ring buffer; at capacity it evicts its oldest row, and walking the live
    ///     collection could no longer find a copy command still awaiting the singleton clipboard.
    /// </summary>
    [Fact]
    public async Task An_evicted_stream_rows_command_is_still_drained()
    {
        var clipboard = new BlockingClipboard(new Blocker());
        var blocker = clipboard.Blocker;

        var docs = new DocumentsViewModel(
            new FakeDescriptorService(), new ImmediateUiDispatcher(), clipboard, new FakeInvocationRunner(),
            new FakeDialogService(), new FakeLauncherService(), new FakeRequestValidator(),
            new InMemorySettingsStore(), new FakeThemeService());

        var tab = new InvocationDocumentViewModel(
            Conn(), "pkg.Svc/Go", "{}", new FakeInvocationRunner(), new FakeDescriptorService(),
            new ImmediateUiDispatcher(), clipboard, new FakeDialogService(), new FakeLauncherService(),
            new FakeRequestValidator(), ringCapacity: 1);

        docs.Documents.Add(tab);

        tab.Log.Append(Event(0));

        var first = tab.Log.Rows[0];
        var copying = first.CopyAsNdjsonCommand.ExecuteAsync(null);

        await blocker.Entered.Task.WaitAsync(Bounded, Ct);

        // The ring evicts the row whose command is still awaiting the singleton clipboard.
        tab.Log.Append(Event(1));

        tab.Log.Rows.ShouldNotContain(first);

        var result = await docs.DisposeOpenDocumentsAsync(ShortDrain).WaitAsync(Bounded, Ct);

        try
        {
            result.Drained.ShouldBeFalse("the evicted row's command is still running");
        }
        finally
        {
            blocker.Release.SetResult();

            await copying.WaitAsync(Bounded, Ct);
        }
    }

    private static StreamEventModel Event(int index)
        => new(StreamEventKind.MessageReceived, index, DateTimeOffset.UtcNow, index, $"preview {index}");

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

    private sealed class BlockingClipboard(Blocker blocker) : IClipboardService
    {
        public Blocker Blocker { get; } = blocker;

        public async Task SetTextAsync(string text, CancellationToken cancellationToken = default)
            => await Blocker.EnterAsync();

        public Task<string?> GetTextAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<string?>(null);
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
