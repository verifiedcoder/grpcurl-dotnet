using GrpCurl.Net.Studio.Services;
using GrpCurl.Net.Studio.TestSupport;
using GrpCurl.Net.Studio.ViewModels;
using GrpCurl.Net.Studio.ViewModels.Documents;
using GrpCurl.Net.Studio.ViewModels.Models;
using GrpCurl.Net.Studio.ViewModels.Models.Connections;
using GrpCurl.Net.Studio.ViewModels.Models.Descriptors;
using GrpCurl.Net.Studio.ViewModels.Models.History;
using GrpCurl.Net.Studio.ViewModels.Models.Invocation;
using GrpCurl.Net.Studio.ViewModels.Models.Session;
using GrpCurl.Net.Studio.ViewModels.Services;
using System.Diagnostics;

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
        IHistoryStore? history = null,
        ISessionStore? session = null,
        TimeSpan? sessionDebounce = null)
        => new(
            descriptors ?? new FakeDescriptorService(), new ImmediateUiDispatcher(), new FakeClipboardService(),
            new FakeInvocationRunner(), new FakeDialogService(), new FakeLauncherService(),
            validator ?? new FakeRequestValidator(), new InMemorySettingsStore(), new FakeThemeService(),
            history: history, secrets: secrets, session: session,
            workspace: new FakeWorkspaceStore(new WorkspaceModel { Id = "w" }),
            // Long by default: a debounce short enough to fire on its own turns "exactly one final write"
            // into a scheduler race, which is what round 12's finding 3 caught. Cases that need the
            // debounce to actually reach the store ask for a short one and gate on it (round 12).
            sessionDebounce: sessionDebounce ?? TimeSpan.FromSeconds(10));

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
    ///     PRD-005 re-review rounds 5, 6 and 8, finding 1: a tab opened by work the drain is already
    ///     waiting on must never be left running.
    ///     <para>
    ///         Round 5 grew the participant list, which covered it only when a round <em>completed</em>.
    ///         Round 6 closed admission at the commit, so a losing tab was built and then retired — and
    ///         that retirement was owned by nobody, so it could outlive the shutdown that refused it.
    ///         Round 8 moved the decision to an opener lease taken before anything is constructed: a
    ///         losing opener returns having built nothing at all.
    ///     </para>
    ///     <para>
    ///         The source load stays blocked through the timeout, which is the case round 6 found. Two
    ///         observables: the descriptor service is never asked to resolve the probe method (nothing
    ///         was built), and the environment singleton's subscriber count never moves (nothing
    ///         subscribed, so nothing is left subscribed).
    ///     </para>
    /// </summary>
    [Fact]
    public async Task A_tab_opened_by_admitted_work_is_refused_before_it_is_built()
    {
        var source = new Blocker();

        DocumentsViewModel? docs = null;

        var environment = new EnvironmentService(
            new FakeWorkspaceStore(new WorkspaceModel
            {
                Environments = [new WorkspaceEnvironment { Id = "e1", Name = "staging" }]
            }),
            new FakeSecretStore());

        var probeResolved = false;

        var descriptors = new FakeDescriptorService
        {
            OnDescribe = async (_, symbol, _) =>
            {
                if (symbol == "pkg.Source")
                {
                    // Parked when the drain starts, so this load is admitted work; it then tries to open
                    // a tab and stays blocked past the timeout.
                    await source.EnterAsync();

                    docs!.OpenInvocation(Conn(), "pkg.Svc/Probe", "{}");
                }
                else
                {
                    probeResolved = true;
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

        _ = await shutdown.WaitAsync(Bounded, Ct);

        docs.Documents.ShouldNotContain(d => d is InvocationDocumentViewModel,
            "admission is closed once shutdown starts");

        probeResolved.ShouldBeFalse("a refused opener must not construct a tab at all");

        environment.ActiveChangedSubscribers.ShouldBe(baseline);
    }

    /// <summary>
    ///     PRD-005 re-review round 8, finding 1: shutdown must wait for an opener that was <em>already
    ///     admitted</em> — the whole operation, not just its collection commit.
    ///     <para>
    ///         The lease is taken before the tab is constructed, and this test parks the opener inside
    ///         that construction by blocking the settings store the invocation constructor reads. Round
    ///         7's gate covered only the commit, so shutdown could declare quiescence while an opener was
    ///         still building — or, worse, still disposing a tab it had been refused.
    ///     </para>
    ///     <para>
    ///         What the wait buys is cleanup, not admission: the tab is retired rather than committed,
    ///         because after shutdown begins nothing joins <c>Documents</c>. Shutdown simply must not
    ///         finish until that retirement has.
    ///     </para>
    /// </summary>
    [Fact]
    public async Task Shutdown_waits_for_an_opener_that_was_already_admitted()
    {
        var construction = new Blocker();

        var environment = new EnvironmentService(
            new FakeWorkspaceStore(new WorkspaceModel
            {
                Environments = [new WorkspaceEnvironment { Id = "e1", Name = "staging" }]
            }),
            new FakeSecretStore());

        var docs = new DocumentsViewModel(
            new FakeDescriptorService(), new ImmediateUiDispatcher(), new FakeClipboardService(),
            new FakeInvocationRunner(), new FakeDialogService(), new FakeLauncherService(),
            new FakeRequestValidator(), new BlockingReadSettingsStore(construction), new FakeThemeService(),
            environment: environment);

        var baseline = environment.ActiveChangedSubscribers;

        // Holds a lease and blocks inside the open operation, before the tab reaches Documents.
        var opening = Task.Run(() => docs.OpenInvocation(Conn(), "pkg.Svc/Go", "{}"), Ct);

        await construction.Entered.Task.WaitAsync(Bounded, Ct);

        var shutdown = docs.DisposeOpenDocumentsAsync(Bounded);

        await Task.Delay(250, Ct);

        shutdown.IsCompleted.ShouldBeFalse("shutdown must wait for an opener it already admitted");

        construction.Release.SetResult();

        var result = await shutdown.WaitAsync(Bounded, Ct);

        await opening.WaitAsync(Bounded, Ct);

        result.Drained.ShouldBeTrue("the opener finished well inside the budget");

        // Waited for, but not admitted: once shutdown has begun no tab joins the collection, so the
        // opener retires what it built rather than committing it.
        docs.Documents.ShouldBeEmpty();

        environment.ActiveChangedSubscribers.ShouldBe(baseline,
            "the tab shutdown waited for must also have been disposed");
    }

    /// <summary>
    ///     PRD-005 re-review round 9, finding 1: session restore constructs documents and awaits the
    ///     singleton session store, so it is an open operation in everything but its name.
    ///     <para>
    ///         <c>App</c> starts it fire-and-forget at launch. Closing the window while it is still
    ///         loading left the drain with no document, no opener and no tracked task to see, so it could
    ///         report success while restore was still inside the store — the round-8 ownership error
    ///         reached through a method not called <c>Open*</c>.
    ///     </para>
    ///     <para>
    ///         The positive wait is the assertion: shutdown stays incomplete until the load is released,
    ///         and only then reports <c>Drained: true</c>. Proving merely that no late tab appeared would
    ///         not distinguish this from a shutdown that never waited at all.
    ///     </para>
    /// </summary>
    [Fact]
    public async Task Shutdown_waits_for_a_session_restore_that_is_still_loading()
    {
        var loading = new Blocker();

        var docs = new DocumentsViewModel(
            new FakeDescriptorService(), new ImmediateUiDispatcher(), new FakeClipboardService(),
            new FakeInvocationRunner(), new FakeDialogService(), new FakeLauncherService(),
            new FakeRequestValidator(), new InMemorySettingsStore(), new FakeThemeService(),
            workspace: new FakeWorkspaceStore(new WorkspaceModel()), session: new BlockingSessionStore(loading));

        // Fire-and-forget, exactly as App does at launch.
        var restoring = docs.RestoreSessionAsync();

        await loading.Entered.Task.WaitAsync(Bounded, Ct);

        var shutdown = docs.DisposeOpenDocumentsAsync(Bounded);

        await Task.Delay(250, Ct);

        shutdown.IsCompleted.ShouldBeFalse("shutdown must wait for a restore that is still loading");

        loading.Release.SetResult();

        var result = await shutdown.WaitAsync(Bounded, Ct);

        await restoring.WaitAsync(Bounded, Ct);

        result.Drained.ShouldBeTrue("the restore finished well inside the budget");
    }

    /// <summary>
    ///     PRD-005 re-review round 10, finding 1: the final session snapshot must not overtake a startup
    ///     restore that is still loading.
    ///     <para>
    ///         <c>Program</c> used to flush and then drain. Quitting during restore therefore snapshotted
    ///         an empty <c>Documents</c> — the restore was still parked in the session store — and wrote
    ///         it over the very file being restored. The user's tabs were destroyed by the shutdown meant
    ///         to preserve them.
    ///     </para>
    ///     <para>
    ///         This drives <see cref="DocumentsViewModel.ShutdownAsync" />, the coordinator <c>Program</c>
    ///         now calls, rather than the drain alone: the ordering between the flush and the wait is the
    ///         thing under test, and a direct drain test cannot see it.
    ///     </para>
    ///     <para>
    ///         Note what "restored" has to mean. Releasing the load lets <c>RestoreSessionAsync</c>
    ///         <em>return</em>, but its tabs are refused by the admission it is racing — so a completion
    ///         flag set on return would still persist an empty snapshot. The flag is set only when
    ///         shutdown had not begun.
    ///     </para>
    /// </summary>
    [Fact]
    public async Task Shutdown_does_not_overwrite_a_session_it_never_finished_restoring()
    {
        var loading = new Blocker();

        var prior = new SessionState
        {
            WorkspaceId = "w",
            Tabs = [new SessionTab(SessionTabKind.Describe, "c1", "pkg.Alpha")]
        };

        var store = new BlockingSessionStore(loading) { State = prior };

        var docs = new DocumentsViewModel(
            new FakeDescriptorService(), new ImmediateUiDispatcher(), new FakeClipboardService(),
            new FakeInvocationRunner(), new FakeDialogService(), new FakeLauncherService(),
            new FakeRequestValidator(), new InMemorySettingsStore(), new FakeThemeService(),
            workspace: new FakeWorkspaceStore(new WorkspaceModel
            {
                Id = "w",
                // The saved tab references c1; without it the restore skips the tab before it can reach
                // the admission refusal this test is about (round 11, finding 3).
                Connections = [new SavedConnection { Id = "c1", Name = "c", Address = "h:1" }]
            }),
            session: store);

        var restoring = docs.RestoreSessionAsync();

        await loading.Entered.Task.WaitAsync(Bounded, Ct);

        var shutdown = docs.ShutdownAsync(Bounded);

        await Task.Delay(250, Ct);

        store.Saves.ShouldBe(0, "the flush must not overtake the load it depends on");
        shutdown.IsCompleted.ShouldBeFalse();

        loading.Release.SetResult();

        var result = await shutdown.WaitAsync(Bounded, Ct);

        await restoring.WaitAsync(Bounded, Ct);

        result.Drained.ShouldBeTrue();

        // Releasing the load does not make the restore real: its tabs were refused by closed admission,
        // so the collection cannot describe the session and the durable file is left exactly as it was.
        result.SessionPersisted.ShouldBeFalse();
        store.Saves.ShouldBe(0, "the one-tab session on disk must survive the shutdown that interrupted it");
    }

    /// <summary>
    ///     The other half: if the restore never finishes inside the budget, the durable file is left
    ///     alone rather than replaced by a snapshot known not to describe it.
    /// </summary>
    [Fact]
    public async Task An_unfinished_restore_leaves_the_previous_session_on_disk()
    {
        var loading = new Blocker();

        var store = new BlockingSessionStore(loading)
        {
            State = new SessionState
            {
                WorkspaceId = "w",
                Tabs = [new SessionTab(SessionTabKind.Describe, "c1", "pkg.Alpha")]
            }
        };

        var docs = new DocumentsViewModel(
            new FakeDescriptorService(), new ImmediateUiDispatcher(), new FakeClipboardService(),
            new FakeInvocationRunner(), new FakeDialogService(), new FakeLauncherService(),
            new FakeRequestValidator(), new InMemorySettingsStore(), new FakeThemeService(),
            workspace: new FakeWorkspaceStore(new WorkspaceModel
            {
                Id = "w",
                // The saved tab references c1; without it the restore skips the tab before it can reach
                // the admission refusal this test is about (round 11, finding 3).
                Connections = [new SavedConnection { Id = "c1", Name = "c", Address = "h:1" }]
            }),
            session: store);

        var restoring = docs.RestoreSessionAsync();

        await loading.Entered.Task.WaitAsync(Bounded, Ct);

        var result = await docs.ShutdownAsync(ShortDrain).WaitAsync(Bounded, Ct);

        try
        {
            result.SessionPersisted.ShouldBeFalse("a snapshot taken mid-restore cannot describe the session");
            store.Saves.ShouldBe(0, "the previously saved session must be left exactly as it was");
        }
        finally
        {
            loading.Release.SetResult();

            await restoring.WaitAsync(Bounded, Ct);
        }
    }

    /// <summary>
    ///     PRD-005 re-review round 11, finding 1: a debounced save that has already passed its delay is
    ///     admitted — cancelling its token does not unwind it — so the final snapshot must wait for it.
    ///     <para>
    ///         Otherwise the older writer lands last and replaces the final session, while shutdown
    ///         reports <c>SessionPersisted: true</c>. Both writers also share one temp path, so racing
    ///         them risks an I/O failure as well as a lost update. The assertion inspects the durable
    ///         state after the older writer is released, not merely that the final save was called.
    ///     </para>
    /// </summary>
    [Fact]
    public async Task An_older_debounced_save_cannot_overwrite_the_final_session()
    {
        var firstSave = new Blocker();
        var store = new OverlappingSessionStore(firstSave);

        // Short on purpose: this case needs the debounced write to reach the store, and it gates on
        // FirstSaveEntered rather than on timing.
        var docs = Docs(session: store, sessionDebounce: TimeSpan.FromMilliseconds(1));

        docs.OpenInvocation(Conn(), "pkg.Svc/Go", "{}");

        var tab = (InvocationDocumentViewModel)docs.Documents[0];

        // A debounced persist that snapshots the old draft and then parks inside the store.
        tab.RequestJson = "old";

        await store.FirstSaveEntered.Task.WaitAsync(Bounded, Ct);

        tab.RequestJson = "new";

        var shutdown = docs.ShutdownAsync(Bounded);

        await Task.Delay(150, Ct);

        store.Saves.ShouldBe(1, "the final write must not start while the older one is still going");

        firstSave.Release.SetResult();

        var result = await shutdown.WaitAsync(Bounded, Ct);

        result.SessionPersisted.ShouldBeTrue();
        store.LastSaved.ShouldNotBeNull().Tabs[0].Body.ShouldBe("new", "the final snapshot must be the durable one");
    }

    /// <summary>
    ///     Round 11, finding 2: a failing session write must not cost the tabs their disposal — the same
    ///     interrupted-cleanup class already handled for capture writers and individual tabs.
    /// </summary>
    [Fact]
    public async Task A_failing_session_write_is_reported_and_the_tabs_are_still_disposed()
    {
        var environment = new EnvironmentService(
            new FakeWorkspaceStore(new WorkspaceModel
            {
                Environments = [new WorkspaceEnvironment { Id = "e1", Name = "staging" }]
            }),
            new FakeSecretStore());

        var docs = new DocumentsViewModel(
            new FakeDescriptorService(), new ImmediateUiDispatcher(), new FakeClipboardService(),
            new FakeInvocationRunner(), new FakeDialogService(), new FakeLauncherService(),
            new FakeRequestValidator(), new InMemorySettingsStore(), new FakeThemeService(),
            workspace: new FakeWorkspaceStore(new WorkspaceModel { Id = "w" }),
            session: new ThrowingSessionStore(), environment: environment);

        docs.OpenInvocation(Conn(), "pkg.Svc/Go", "{}");

        var baseline = environment.ActiveChangedSubscribers;

        baseline.ShouldBeGreaterThan(0, "the tab must actually be subscribed for this to prove anything");

        var result = await docs.ShutdownAsync(Bounded).WaitAsync(Bounded, Ct);

        result.SessionPersisted.ShouldBeFalse("the write failed");
        result.Documents.ShouldBe(1);

        environment.ActiveChangedSubscribers.ShouldBe(0, "a failed save must not skip disposal");
    }

    /// <summary>
    ///     Round 11, finding 3: nothing proved the coordinator ever persists a <em>successful</em> final
    ///     session. Disabling that branch left both interrupted-restore tests green.
    /// </summary>
    [Fact]
    public async Task Shutdown_persists_the_final_session_exactly_once()
    {
        var store = new RecordingSessionStore();

        var docs = Docs(session: store);

        docs.OpenInvocation(Conn(), "pkg.Svc/Go", "{}");

        var tab = (InvocationDocumentViewModel)docs.Documents[0];

        tab.RequestJson = "the live draft";

        var result = await docs.ShutdownAsync(Bounded).WaitAsync(Bounded, Ct);

        result.SessionPersisted.ShouldBeTrue();
        result.Drained.ShouldBeTrue();
        result.Documents.ShouldBe(1);

        store.Saves.ShouldBe(1,
            "exactly one final write: the debounce is long enough that only shutdown's write can reach "
            + "the store, so this counts the coordinator rather than the scheduler");

        var saved = store.LastSaved.ShouldNotBeNull();

        saved.WorkspaceId.ShouldBe("w");
        saved.ActiveTabIndex.ShouldBe(0);
        saved.Tabs.ShouldHaveSingleItem().Body.ShouldBe("the live draft");
    }

    /// <summary>
    ///     PRD-005 re-review round 12, finding 1: a final save that outlives its wait is still work.
    ///     <para>
    ///         Round 11 wrapped the write in <c>WaitAsync</c> and enrolled the task in nothing, and passed
    ///         <c>CancellationToken.None</c> — so a timeout neither stopped the save nor made it visible.
    ///         The drain saw no outstanding work and reported <c>Drained: true</c> while
    ///         <c>SaveAsync</c> was mid temp-file protocol, after which <c>Program</c> is entitled to
    ///         release the lock, dispose the host and call <c>Environment.Exit(0)</c> through it.
    ///     </para>
    ///     <para>
    ///         Both halves are asserted: persistence did not finish, <b>and</b> the outstanding save
    ///         prevents a claim of full drain. The second half needs a fresh drain to be worth anything —
    ///         see the comment at the assertion.
    ///     </para>
    /// </summary>
    [Fact]
    public async Task A_final_save_that_outlives_its_wait_keeps_shutdown_from_claiming_a_drain()
    {
        var save = new Blocker();
        var store = new BlockingFinalSaveStore(save);

        var docs = Docs(session: store);

        docs.OpenInvocation(Conn(), "pkg.Svc/Go", "{}");

        var result = await docs.ShutdownAsync(ShortDrain).WaitAsync(Bounded, Ct);

        try
        {
            await save.Entered.Task.WaitAsync(Bounded, Ct);

            result.SessionPersisted.ShouldBeFalse("the write never finished");
            result.Drained.ShouldBeFalse("a save still inside the store is outstanding work");

            // The assertion above is true even if the save is untracked, because the stalled write
            // consumes the whole budget and the drain then reports false for want of time. Ablating the
            // enrolment showed exactly that, so the discriminating check is a *fresh* drain with a real
            // budget: only a tracked save keeps it from reporting quiescence.
            var afterwards = await docs
                .DisposeOpenDocumentsAsync(ShortDrain)
                .WaitAsync(Bounded, Ct);

            afterwards.Drained.ShouldBeFalse("the running save must be visible to the drain, not merely outlast it");
        }
        finally
        {
            save.Release.SetResult();
        }
    }

    /// <summary>
    ///     PRD-005 re-review round 13, finding 1: session-write admission and ownership must be one
    ///     critical section.
    ///     <para>
    ///         Round 12 checked a closed flag and started the write afterwards. An async method runs its
    ///         synchronous prefix before handing back a task, and the production <c>JsonSessionStore</c>
    ///         creates its directory and temp file in exactly that prefix — so an admitted flush was
    ///         already inside the store while absent from both work sets, and the coordinator started a
    ///         second writer against the same path.
    ///     </para>
    ///     <para>
    ///         The fake blocks its thread inside that prefix to model it. This is the same check-then-act
    ///         defect as round 7's document commit, one subsystem over.
    ///     </para>
    /// </summary>
    [Fact]
    public async Task An_admitted_flush_is_waited_for_before_the_final_save()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var store = new SynchronouslyBlockingStore(entered, release);
        var docs = Docs(session: store);

        docs.OpenInvocation(Conn(), "pkg.Svc/Go", "{}");

        // Blocks its thread inside SaveAsync's synchronous prefix, exactly as JsonSessionStore does its
        // directory/temp-file work before the first suspension.
        var flushing = Task.Run(() => docs.FlushSessionAsync().GetAwaiter().GetResult(), Ct);

        await entered.Task.WaitAsync(Bounded, Ct);

        var shutdown = docs.ShutdownAsync(Bounded);

        await Task.Delay(200, Ct);

        try
        {
            store.Saves.ShouldBe(1, "the coordinator must not start a second writer against the same store");
            shutdown.IsCompleted.ShouldBeFalse("an admitted writer is still inside the store");
        }
        finally
        {
            release.SetResult();

            await flushing.WaitAsync(Bounded, Ct);
            _ = await shutdown.WaitAsync(Bounded, Ct);
        }
    }

    /// <summary>
    ///     PRD-005 re-review round 13, finding 2: asking the save to stop must not itself break shutdown.
    ///     <para>
    ///         <c>CancellationTokenSource.Cancel()</c> runs registered callbacks on the calling thread and
    ///         surfaces an <c>AggregateException</c> if one throws — and a sibling <c>catch</c> cannot
    ///         catch what is thrown from another <c>catch</c> block. The result was the round-11
    ///         interrupted-cleanup outcome again: no disposal, no lock release, no <c>StopAsync</c>.
    ///     </para>
    ///     <para>
    ///         Disposal is observed through the environment singleton's subscriber count, as in the
    ///         throwing-save case, rather than inferred from the absence of an exception.
    ///     </para>
    /// </summary>
    [Fact]
    public async Task A_throwing_cancellation_callback_does_not_abort_teardown()
    {
        var environment = new EnvironmentService(
            new FakeWorkspaceStore(new WorkspaceModel
            {
                Environments = [new WorkspaceEnvironment { Id = "e1", Name = "staging" }]
            }),
            new FakeSecretStore());

        var docs = new DocumentsViewModel(
            new FakeDescriptorService(), new ImmediateUiDispatcher(), new FakeClipboardService(),
            new FakeInvocationRunner(), new FakeDialogService(), new FakeLauncherService(),
            new FakeRequestValidator(), new InMemorySettingsStore(), new FakeThemeService(),
            session: new ThrowingCancellationCallbackStore(),
            workspace: new FakeWorkspaceStore(new WorkspaceModel { Id = "w" }),
            sessionDebounce: TimeSpan.FromSeconds(10), environment: environment);

        docs.OpenInvocation(Conn(), "pkg.Svc/Go", "{}");

        environment.ActiveChangedSubscribers.ShouldBeGreaterThan(0);

        var result = await docs.ShutdownAsync(TimeSpan.FromMilliseconds(200)).WaitAsync(Bounded, Ct);

        result.SessionPersisted.ShouldBeFalse();
        environment.ActiveChangedSubscribers.ShouldBe(0, "phase 3 must still dispose the tabs");

        // Cleanup observed rather than assumed: the save honoured the token it was asked to stop on, so
        // a fresh drain settles instead of the test passing with work rooted for the process's life.
        var afterwards = await docs.DisposeOpenDocumentsAsync(Bounded).WaitAsync(Bounded, Ct);

        afterwards.Drained.ShouldBeTrue("the cancelled final save settled");
    }

    /// <summary>
    ///     Round 12, finding 1, second half: the timeout must actually <em>ask</em> the save to stop.
    ///     A store that honours its token ends when cancellation is requested, so the drain settles
    ///     afterwards; with <c>CancellationToken.None</c> — what round 11 passed — it never would.
    /// </summary>
    [Fact]
    public async Task A_timed_out_final_save_is_asked_to_cancel()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var store = new CooperativeSessionStore(entered);

        var docs = Docs(session: store);

        docs.OpenInvocation(Conn(), "pkg.Svc/Go", "{}");

        var result = await docs.ShutdownAsync(ShortDrain).WaitAsync(Bounded, Ct);

        await entered.Task.WaitAsync(Bounded, Ct);

        result.SessionPersisted.ShouldBeFalse("the write did not finish inside the budget");

        // Cancellation was requested at the timeout, so a store that observes its token has stopped and
        // a later drain can settle. Passing None leaves it running for ever.
        var afterwards = await docs.DisposeOpenDocumentsAsync(Bounded).WaitAsync(Bounded, Ct);

        afterwards.Drained.ShouldBeTrue("the cancelled save must have unwound");
    }

    /// <summary>
    ///     Round 12, finding 1, third half: session-write admission closes with document admission, so a
    ///     direct <c>FlushSessionAsync</c> cannot start a second writer against the same file.
    /// </summary>
    [Fact]
    public async Task A_flush_after_shutdown_does_not_write()
    {
        var store = new RecordingSessionStore();

        var docs = Docs(session: store);

        docs.OpenInvocation(Conn(), "pkg.Svc/Go", "{}");

        _ = await docs.ShutdownAsync(Bounded).WaitAsync(Bounded, Ct);

        store.Saves.ShouldBe(1, "the coordinator's own final write");

        await docs.FlushSessionAsync(Ct);

        store.Saves.ShouldBe(1, "a flush after shutdown must be refused, not raced against the final write");
    }

    /// <summary>
    ///     Round 12, finding 2: the coordinator's budget is one ceiling, not one per wait.
    ///     <para>
    ///         An earlier writer consumes most of the budget and the final writer never returns. Correct
    ///         behaviour finishes at about the budget; giving each wait its own copy took 1902ms against
    ///         a 1000ms budget when this was measured against the unfixed code. The bound below sits
    ///         between the two with room for scheduling on a loaded machine — it is a wall-clock
    ///         assertion by necessity, since the defect is entirely about elapsed time.
    ///     </para>
    /// </summary>
    [Fact]
    public async Task The_coordinator_stays_within_one_budget_across_both_persistence_waits()
    {
        var budget = TimeSpan.FromSeconds(1);

        var firstEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finalRelease = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var store = new SlowThenStalledSessionStore(firstEntered, finalRelease, TimeSpan.FromMilliseconds(900));

        var docs = Docs(session: store, sessionDebounce: TimeSpan.FromMilliseconds(1));

        docs.OpenInvocation(Conn(), "pkg.Svc/Go", "{}");

        ((InvocationDocumentViewModel)docs.Documents[0]).RequestJson = "old";

        await firstEntered.Task.WaitAsync(Bounded, Ct);

        var clock = Stopwatch.StartNew();

        _ = await docs.ShutdownAsync(budget).WaitAsync(Bounded, Ct);

        clock.Stop();

        try
        {
            clock.Elapsed.ShouldBeLessThan(
                TimeSpan.FromMilliseconds(1500),
                $"the whole coordinator must fit in its budget; took {clock.ElapsedMilliseconds}ms");
        }
        finally
        {
            finalRelease.SetResult();
        }
    }

    /// <summary>A restore started after shutdown must not even reach the session store.</summary>
    [Fact]
    public async Task A_session_restore_started_after_shutdown_loads_nothing()
    {
        var loading = new Blocker();
        var store = new BlockingSessionStore(loading);

        var docs = new DocumentsViewModel(
            new FakeDescriptorService(), new ImmediateUiDispatcher(), new FakeClipboardService(),
            new FakeInvocationRunner(), new FakeDialogService(), new FakeLauncherService(),
            new FakeRequestValidator(), new InMemorySettingsStore(), new FakeThemeService(),
            workspace: new FakeWorkspaceStore(new WorkspaceModel()), session: store);

        _ = await docs.DisposeOpenDocumentsAsync(ShortDrain).WaitAsync(Bounded, Ct);

        await docs.RestoreSessionAsync().WaitAsync(Bounded, Ct);

        store.Loads.ShouldBe(0, "admission is closed, so restore must not touch the singleton at all");
    }

    /// <summary>
    ///     PRD-005 re-review round 8: an opener slower than the bounded drain must not commit a live tab
    ///     after shutdown has given up waiting for it.
    ///     <para>
    ///         The lease keeps a <em>merely slow</em> opener ahead of shutdown, but the drain has a
    ///         budget: once it expires, shutdown returns while the lease is still held. The commit
    ///         therefore re-checks the flag rather than trusting the lease it entered under, and retires
    ///         instead of adding.
    ///     </para>
    /// </summary>
    [Fact]
    public async Task An_opener_slower_than_the_drain_budget_retires_instead_of_committing()
    {
        var construction = new Blocker();

        var environment = new EnvironmentService(
            new FakeWorkspaceStore(new WorkspaceModel
            {
                Environments = [new WorkspaceEnvironment { Id = "e1", Name = "staging" }]
            }),
            new FakeSecretStore());

        var docs = new DocumentsViewModel(
            new FakeDescriptorService(), new ImmediateUiDispatcher(), new FakeClipboardService(),
            new FakeInvocationRunner(), new FakeDialogService(), new FakeLauncherService(),
            new FakeRequestValidator(), new BlockingReadSettingsStore(construction), new FakeThemeService(),
            environment: environment);

        var baseline = environment.ActiveChangedSubscribers;

        var opening = Task.Run(() => docs.OpenInvocation(Conn(), "pkg.Svc/Go", "{}"), Ct);

        await construction.Entered.Task.WaitAsync(Bounded, Ct);

        // The drain gives up while the lease is still held.
        var result = await docs.DisposeOpenDocumentsAsync(ShortDrain).WaitAsync(Bounded, Ct);

        result.Drained.ShouldBeFalse("the opener was still holding its lease");

        construction.Release.SetResult();

        await opening.WaitAsync(Bounded, Ct);

        docs.Documents.ShouldBeEmpty("a tab must not join the collection after shutdown has returned");

        environment.ActiveChangedSubscribers.ShouldBe(baseline, "the late tab must have been retired");
    }

    /// <summary>
    ///     PRD-005 re-review round 7: the shutdown transition and the collection commit must be one
    ///     critical section, not a check followed by an add.
    ///     <para>
    ///         Round 6 read a plain flag and let each caller add afterwards. An opener could pass the
    ///         check, shutdown could then set the flag and complete an empty drain, and the opener could
    ///         commit a live tab <em>after</em> shutdown had returned — a tab nothing would ever dispose.
    ///     </para>
    ///     <para>
    ///         Contention, repeated: each round starts an opener and a shutdown on a barrier so they
    ///         reach the boundary together. Emptiness is <em>not</em> the invariant — shutdown leaves
    ///         <c>Documents</c> alone on purpose so the session snapshot survives — so what is asserted
    ///         is that nothing is left <b>undisposed</b>, via the environment singleton's subscriber
    ///         count.
    ///     </para>
    ///     <para>
    ///         <b>Its sensitivity is measured rather than assumed, and it differs by defect.</b> Against
    ///         a drain that ignores opener leases (ablation AJ) it fails deterministically. Against the
    ///         older non-atomic commit (ablation AH) it caught the defect in one run out of three: that
    ///         window is a handful of instructions wide, so for <em>that</em> defect a green run is weak
    ///         evidence and the guarantee comes from the critical section in <c>AdmitAndAdd</c> instead.
    ///     </para>
    /// </summary>
    [Fact]
    public async Task An_opener_racing_shutdown_is_either_disposed_with_it_or_retired()
    {
        for (var attempt = 0; attempt < 200; attempt++)
        {
            var environment = new EnvironmentService(
                new FakeWorkspaceStore(new WorkspaceModel
                {
                    Environments = [new WorkspaceEnvironment { Id = "e1", Name = "staging" }]
                }),
                new FakeSecretStore());

            var docs = new DocumentsViewModel(
                new FakeDescriptorService(), new ImmediateUiDispatcher(), new FakeClipboardService(),
                new FakeInvocationRunner(), new FakeDialogService(), new FakeLauncherService(),
                new FakeRequestValidator(), new InMemorySettingsStore(), new FakeThemeService(),
                environment: environment);

            var baseline = environment.ActiveChangedSubscribers;

            using var barrier = new Barrier(2);

            var opening = Task.Run(() =>
            {
                barrier.SignalAndWait();

                docs.OpenInvocation(Conn(), "pkg.Svc/Probe", "{}");
            }, Ct);

            var shutdown = Task.Run(() =>
            {
                barrier.SignalAndWait();

                return docs.DisposeOpenDocumentsAsync(Bounded);
            }, Ct);

            // Shutdown first, and the assertion before the opener is awaited: the claim is about the
            // instant shutdown returns. Awaiting the opener first would let retirement finish and make
            // the assertion vacuous for exactly the interleaving it exists to watch (round 8 review).
            var outcome = await shutdown.WaitAsync(Bounded, Ct);

            environment.ActiveChangedSubscribers.ShouldBe(baseline,
                $"attempt {attempt}: participants={outcome.Documents} drained={outcome.Drained} "
                + $"docs={docs.Documents.Count}");

            await opening.WaitAsync(Bounded, Ct);
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

    /// <summary>
    ///     PRD-005 re-review round 14, finding 1: the session-writer lease must be part of <b>every</b>
    ///     drain round, not only the coordinator's persistence phase.
    ///     <para>
    ///         Round 13 gave session writes a lease so admission and ownership were one critical section,
    ///         and taught <c>PersistFinalSessionAsync</c> to wait for it. The general drain still built its
    ///         round from the work sets and the opener lease alone, so a caller who went straight to
    ///         <see cref="DocumentsViewModel.DisposeOpenDocumentsAsync" /> — or a coordinator run that
    ///         skipped persistence — saw no participants, no enrolled work, and reported
    ///         <c>Drained: true</c> over a writer sitting in the store's synchronous prefix.
    ///     </para>
    /// </summary>
    [Fact]
    public async Task A_direct_drain_waits_for_an_admitted_session_writer()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var store = new SynchronouslyBlockingStore(entered, release);
        var docs = Docs(session: store);

        // Blocks its thread inside SaveAsync before any task exists to enrol: the lease is the only
        // thing that can know about this writer.
        var flushing = Task.Run(() => docs.FlushSessionAsync().GetAwaiter().GetResult(), Ct);

        await entered.Task.WaitAsync(Bounded, Ct);

        var result = await docs.DisposeOpenDocumentsAsync(ShortDrain).WaitAsync(Bounded, Ct);

        try
        {
            result.Drained.ShouldBeFalse("an admitted session writer is still inside the store");
        }
        finally
        {
            release.SetResult();

            await flushing.WaitAsync(Bounded, Ct);
        }

        // And the same drain tells the truth in the other direction once the writer has left, so the
        // assertion above cannot be satisfied by a drain that simply never succeeds.
        var afterwards = await docs.DisposeOpenDocumentsAsync(Bounded).WaitAsync(Bounded, Ct);

        afterwards.Drained.ShouldBeTrue("the writer released its lease");
    }

    /// <summary>
    ///     Round 14, finding 1, second half: a coordinator run that deliberately skips persistence must
    ///     still report an admitted writer, because phase 3 is then the only drain there is.
    ///     <para>
    ///         An interrupted restore skips phase 2 to protect the session on disk. The phase-2 lease wait
    ///         is skipped with it, so if the general drain does not own the lease, nothing does.
    ///     </para>
    /// </summary>
    [Fact]
    public async Task A_shutdown_that_skips_persistence_still_waits_for_an_admitted_writer()
    {
        var loading = new Blocker();

        var savedEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var savedRelease = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var store = new BlockingLoadBlockingSaveStore(loading, savedEntered, savedRelease);

        var docs = new DocumentsViewModel(
            new FakeDescriptorService(), new ImmediateUiDispatcher(), new FakeClipboardService(),
            new FakeInvocationRunner(), new FakeDialogService(), new FakeLauncherService(),
            new FakeRequestValidator(), new InMemorySettingsStore(), new FakeThemeService(),
            workspace: new FakeWorkspaceStore(new WorkspaceModel { Id = "w" }),
            session: store, sessionDebounce: TimeSpan.FromSeconds(10));

        // A restore in flight, so the run reaches the skip-persistence branch.
        var restoring = docs.RestoreSessionAsync();

        await loading.Entered.Task.WaitAsync(Bounded, Ct);

        // Admitted before shutdown closes admission, and still in the store's synchronous prefix.
        var flushing = Task.Run(() => docs.FlushSessionAsync().GetAwaiter().GetResult(), Ct);

        await savedEntered.Task.WaitAsync(Bounded, Ct);

        var shutdown = docs.ShutdownAsync(ShortDrain);

        // Let the restore return: it was refused by admission, so persistence is skipped.
        loading.Release.SetResult();

        await restoring.WaitAsync(Bounded, Ct);

        var result = await shutdown.WaitAsync(Bounded, Ct);

        try
        {
            result.SessionPersisted.ShouldBeFalse("the restore did not finish, so phase 2 was skipped");
            result.Drained.ShouldBeFalse("the admitted writer is still inside the store");
        }
        finally
        {
            savedRelease.SetResult();

            await flushing.WaitAsync(Bounded, Ct);
        }
    }

    /// <summary>
    ///     Round 14, finding 2: the cancellation request that shutdown makes <em>before</em> the final
    ///     writer exists must be contained too.
    ///     <para>
    ///         Round 13 contained the timeout's request against the final save's own source. The first
    ///         thing <c>PersistFinalSessionAsync</c> does, though, is cancel the pending debounce's
    ///         source — and a throwing callback there escaped before either lease wait, so shutdown never
    ///         reached phase 3. AY cannot see this: its default ten-second debounce means no earlier
    ///         source is ever in play.
    ///     </para>
    ///     <para>
    ///         Only the admitted debounce's cancellation throws here. The final write then succeeds, which
    ///         is what proves the containment let the coordinator carry on rather than merely survive.
    ///     </para>
    /// </summary>
    [Fact]
    public async Task A_throwing_callback_on_an_admitted_debounce_does_not_abort_teardown()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var environment = new EnvironmentService(
            new FakeWorkspaceStore(new WorkspaceModel
            {
                Environments = [new WorkspaceEnvironment { Id = "e1", Name = "staging" }]
            }),
            new FakeSecretStore());

        var store = new ThrowingCallbackOnFirstSaveStore(entered);

        var docs = new DocumentsViewModel(
            new FakeDescriptorService(), new ImmediateUiDispatcher(), new FakeClipboardService(),
            new FakeInvocationRunner(), new FakeDialogService(), new FakeLauncherService(),
            new FakeRequestValidator(), new InMemorySettingsStore(), new FakeThemeService(),
            session: store,
            workspace: new FakeWorkspaceStore(new WorkspaceModel { Id = "w" }),
            sessionDebounce: TimeSpan.FromMilliseconds(1), environment: environment);

        docs.OpenInvocation(Conn(), "pkg.Svc/Go", "{}");

        environment.ActiveChangedSubscribers.ShouldBeGreaterThan(0);

        // The debounce has passed its delay and is inside the store, holding its lease, with a callback
        // registered on the source shutdown is about to cancel.
        await entered.Task.WaitAsync(Bounded, Ct);

        var result = await docs.ShutdownAsync(Bounded).WaitAsync(Bounded, Ct);

        result.SessionPersisted.ShouldBeTrue("the coordinator must carry on to its own write");
        result.Drained.ShouldBeTrue("the cancelled debounce settled and was waited for");
        store.Saves.ShouldBe(2, "the debounce and the final write, in that order");
        environment.ActiveChangedSubscribers.ShouldBe(0, "phase 3 must still dispose the tabs");
    }

    /// <summary>
    ///     Round 14, finding 2, second half: the debounce source must not be disposed while the write it
    ///     cancelled is still holding its token.
    ///     <para>
    ///         <c>DisposeOpenDocumentsAsync</c> cancelled and immediately disposed the shared source, so an
    ///         admitted write that outlived the request held a token whose source was gone.
    ///     </para>
    ///     <para>
    ///         Which API is touched decides whether this is observable, and the first version of this test
    ///         was blind because of it. <c>Register</c> on an <em>already-cancelled</em> token runs the
    ///         callback inline without consulting the source, so it succeeds against the disposed source
    ///         and proves nothing — and cancellation always precedes disposal here.
    ///         <c>WaitHandle</c> does consult it and throws <c>ObjectDisposedException</c>, so that is the
    ///         observable this test uses.
    ///     </para>
    /// </summary>
    [Fact]
    public async Task An_admitted_debounce_keeps_a_usable_token_after_shutdown_cancels_it()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var store = new TokenTouchingStore(entered, release);

        var docs = Docs(session: store, sessionDebounce: TimeSpan.FromMilliseconds(1));

        docs.OpenInvocation(Conn(), "pkg.Svc/Go", "{}");

        await entered.Task.WaitAsync(Bounded, Ct);

        var shutdown = docs.ShutdownAsync(ShortDrain);

        // Shutdown has asked the debounce to stop and given up waiting; the write only now reaches for
        // the token it was handed.
        _ = await shutdown.WaitAsync(Bounded, Ct);

        release.SetResult();

        await store.Finished.Task.WaitAsync(Bounded, Ct);

        store.TokenFailure.ShouldBeNull("the source must outlive the write that holds its token");
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

        /// <summary>For seams reached from synchronous code — a property getter, an event accessor.</summary>
        public void EnterBlocking()
        {
            _ = Entered.TrySetResult();

            Release.Task.GetAwaiter().GetResult();
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

    /// <summary>Blocks the first read of <c>Current</c> — the invocation constructor's first act.</summary>
    private sealed class BlockingReadSettingsStore(Blocker blocker) : ISettingsStore
    {
        private readonly StudioSettings _settings = new();

        private int _reads;

        public StudioSettings Current
        {
            get
            {
                if (Interlocked.Increment(ref _reads) == 1)
                {
                    blocker.EnterBlocking();
                }

                return _settings;
            }
        }

        public event EventHandler? Changed;

        public Task<StudioSettings> LoadAsync(CancellationToken cancellationToken = default)
        {
            Changed?.Invoke(this, EventArgs.Empty);

            return Task.FromResult(_settings);
        }

        public Task SaveAsync(StudioSettings settings, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
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


    /// <summary>Parks inside the first save, ignoring cancellation, and lets later ones through.</summary>
    private sealed class OverlappingSessionStore(Blocker firstSave) : ISessionStore
    {
        private int _saves;

        public TaskCompletionSource FirstSaveEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int Saves => _saves;

        public SessionState? LastSaved { get; private set; }

        public Task<SessionState> LoadAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(new SessionState());

        public async Task SaveAsync(SessionState state, CancellationToken cancellationToken = default)
        {
            if (Interlocked.Increment(ref _saves) == 1)
            {
                _ = FirstSaveEntered.TrySetResult();

                await firstSave.Release.Task; // deliberately not observing the token
            }

            LastSaved = state;
        }
    }

    /// <summary>Signals entry to the final save and then stays inside it, ignoring the token.</summary>
    private sealed class BlockingFinalSaveStore(Blocker save) : ISessionStore
    {
        public Task<SessionState> LoadAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(new SessionState());

        public async Task SaveAsync(SessionState state, CancellationToken cancellationToken = default)
            => await save.EnterAsync();
    }

    /// <summary>The debounced write takes most of the budget; the final one never returns.</summary>
    private sealed class SlowThenStalledSessionStore(
        TaskCompletionSource firstEntered,
        TaskCompletionSource finalRelease,
        TimeSpan firstDuration) : ISessionStore
    {
        private int _saves;

        public Task<SessionState> LoadAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(new SessionState());

        public async Task SaveAsync(SessionState state, CancellationToken cancellationToken = default)
        {
            if (Interlocked.Increment(ref _saves) == 1)
            {
                _ = firstEntered.TrySetResult();

                await Task.Delay(firstDuration, CancellationToken.None);

                return;
            }

            await finalRelease.Task;
        }
    }

    /// <summary>Stays inside the save until its token is cancelled, then ends.</summary>
    private sealed class CooperativeSessionStore(TaskCompletionSource entered) : ISessionStore
    {
        public Task<SessionState> LoadAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(new SessionState());

        public async Task SaveAsync(SessionState state, CancellationToken cancellationToken = default)
        {
            _ = entered.TrySetResult();

            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        }
    }

    /// <summary>Blocks the calling thread inside the save's synchronous prefix.</summary>
    private sealed class SynchronouslyBlockingStore(TaskCompletionSource entered, TaskCompletionSource release) : ISessionStore
    {
        private int _saves;

        public int Saves => _saves;

        public Task<SessionState> LoadAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(new SessionState());

        public Task SaveAsync(SessionState state, CancellationToken cancellationToken = default)
        {
            if (Interlocked.Increment(ref _saves) == 1)
            {
                _ = entered.TrySetResult();

                release.Task.GetAwaiter().GetResult(); // synchronous, before any task is returned
            }

            return Task.CompletedTask;
        }
    }

    private sealed class ThrowingCancellationCallbackStore : ISessionStore
    {
        public Task<SessionState> LoadAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(new SessionState());

        public async Task SaveAsync(SessionState state, CancellationToken cancellationToken = default)
        {
            using var registration = cancellationToken.Register(static () => throw new IOException("callback"));

            // On the supplied token, not None (round 14, finding 3). Cancel() runs every registered
            // callback and aggregates their failures, so the deliberate IOException still exercises
            // containment — while an infinite delay on None left this save, its enrolment in both work
            // sets and the final token source rooted for the life of the test process.
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                // Asked to stop, and did.
            }
        }
    }
    private sealed class ThrowingSessionStore : ISessionStore
    {
        public Task<SessionState> LoadAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(new SessionState());

        public Task SaveAsync(SessionState state, CancellationToken cancellationToken = default)
            => throw new IOException("the session file is unwritable");
    }

    private sealed class RecordingSessionStore : ISessionStore
    {
        public int Saves { get; private set; }

        public SessionState? LastSaved { get; private set; }

        public Task<SessionState> LoadAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(new SessionState());

        public Task SaveAsync(SessionState state, CancellationToken cancellationToken = default)
        {
            Saves++;
            LastSaved = state;

            return Task.CompletedTask;
        }
    }

    /// <summary>
    ///     Blocks the load, then blocks the first save on the calling thread — the two halves round 14's
    ///     skipped-persistence case needs from one store.
    /// </summary>
    private sealed class BlockingLoadBlockingSaveStore(
        Blocker loading,
        TaskCompletionSource savedEntered,
        TaskCompletionSource savedRelease) : ISessionStore
    {
        private int _saves;

        public int Saves => _saves;

        public async Task<SessionState> LoadAsync(CancellationToken cancellationToken = default)
        {
            await loading.EnterAsync();

            return new SessionState();
        }

        public Task SaveAsync(SessionState state, CancellationToken cancellationToken = default)
        {
            if (Interlocked.Increment(ref _saves) == 1)
            {
                _ = savedEntered.TrySetResult();

                savedRelease.Task.GetAwaiter().GetResult(); // synchronous, before any task is returned
            }

            return Task.CompletedTask;
        }
    }

    /// <summary>
    ///     Registers a throwing cancellation callback on the <b>first</b> save only, and honours its token.
    ///     Later saves complete normally, so the test can tell "shutdown survived" from "shutdown carried
    ///     on".
    /// </summary>
    private sealed class ThrowingCallbackOnFirstSaveStore(TaskCompletionSource entered) : ISessionStore
    {
        private int _saves;

        public int Saves => _saves;

        public Task<SessionState> LoadAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(new SessionState());

        public async Task SaveAsync(SessionState state, CancellationToken cancellationToken = default)
        {
            if (Interlocked.Increment(ref _saves) > 1)
            {
                return;
            }

            using var registration = cancellationToken.Register(static () => throw new IOException("callback"));

            _ = entered.TrySetResult();

            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                // The request landed: this writer is done, which is all shutdown asked for.
            }
        }
    }

    /// <summary>
    ///     Touches its token's wait handle after the test releases it, and records whatever that throws.
    /// </summary>
    private sealed class TokenTouchingStore(TaskCompletionSource entered, TaskCompletionSource release) : ISessionStore
    {
        public TaskCompletionSource Finished { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Exception? TokenFailure { get; private set; }

        public Task<SessionState> LoadAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(new SessionState());

        public async Task SaveAsync(SessionState state, CancellationToken cancellationToken = default)
        {
            _ = entered.TrySetResult();

            await release.Task;

            try
            {
                // Not Register: on an already-cancelled token that runs inline and never consults the
                // source, so it cannot tell a live source from a disposed one.
                _ = cancellationToken.WaitHandle;
            }
            catch (Exception ex)
            {
                TokenFailure = ex;
            }

            _ = Finished.TrySetResult();
        }
    }

    private sealed class BlockingSessionStore(Blocker blocker) : ISessionStore
    {
        public int Loads { get; private set; }

        public int Saves { get; private set; }

        public SessionState State { get; init; } = new();

        public SessionState? LastSaved { get; private set; }

        public async Task<SessionState> LoadAsync(CancellationToken cancellationToken = default)
        {
            Loads++;

            await blocker.EnterAsync();

            return State;
        }

        public Task SaveAsync(SessionState state, CancellationToken cancellationToken = default)
        {
            Saves++;
            LastSaved = state;

            return Task.CompletedTask;
        }
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
