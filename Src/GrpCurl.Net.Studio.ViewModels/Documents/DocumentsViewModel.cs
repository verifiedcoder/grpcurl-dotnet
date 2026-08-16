using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GrpCurl.Net.Studio.ViewModels.Connections;
using GrpCurl.Net.Studio.ViewModels.Models;
using GrpCurl.Net.Studio.ViewModels.Models.Connections;
using GrpCurl.Net.Studio.ViewModels.Models.Diagnostics;
using GrpCurl.Net.Studio.ViewModels.Models.Invocation;
using GrpCurl.Net.Studio.ViewModels.Models.Session;
using GrpCurl.Net.Studio.ViewModels.Panes;
using GrpCurl.Net.Studio.ViewModels.Services;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;

namespace GrpCurl.Net.Studio.ViewModels.Documents;

/// <summary>
///     Owns the centre-zone document tabs and implements <see cref="IDocumentHost" />. Opening a
///     describe document de-dupes against an existing tab already showing that symbol unless a new
///     tab is explicitly requested (FR-051 Ctrl+click). Closing a tab selects a sensible neighbour.
/// </summary>
public sealed partial class DocumentsViewModel : ViewModelBase, IDocumentHost
{
    private readonly IDescriptorService _descriptors;
    private readonly IUiDispatcher _dispatcher;
    private readonly IClipboardService _clipboard;
    private readonly IRevealGate? _revealGate;
    private readonly IInvocationRunner _invocation;
    private readonly IGraphQlService? _graphql;
    private readonly IDialogService _dialogs;
    private readonly ILauncherService _launcher;
    private readonly IRequestValidator _validator;
    private readonly ISettingsStore _settings;
    private readonly IThemeService _theme;
    private readonly IProtocService? _protoc;
    private readonly ISecretStore? _secrets;
    private readonly ISavedRequestStore? _savedRequests;
    private readonly IEnvironmentService? _environment;
    private readonly IUpdateService? _updates;
    private readonly IDiagnosticsLog? _diagnostics;
    private readonly IFilePickerService? _filePicker;
    private readonly ConsoleViewModel? _console;
    private readonly IInspector? _inspector;
    private readonly IHistoryRecorder? _recorder;
    private readonly IHistoryStore? _history;
    private readonly IWorkspaceStore? _workspace;
    private readonly ISessionStore? _session;
    private readonly TimeSpan _sessionDebounce;

    /// <summary>
    ///     The pending debounced write's cancellation, and the two-owner lifetime its source needs
    ///     (PRD-005 re-review round 15, finding 1). See <see cref="PersistCancellation" />.
    /// </summary>
    private PersistCancellation? _persistCancellation;
    private bool _suppressPersist;

    /// <summary>
    ///     Linearises the shutdown transition against the collection commit. Both the flag below and the
    ///     add in <see cref="AdmitAndAdd" /> happen inside it, so an opener has exactly two outcomes and
    ///     no third (PRD-005 re-review round 7).
    /// </summary>
    private readonly System.Threading.Lock _admissionGate = new();

    /// <summary>
    ///     Set when shutdown begins, and never cleared: the process is going away. Admission closes with
    ///     it — a tab opened from here on is retired instead of joining <see cref="Documents" />
    ///     (PRD-005 re-review round 6, finding 1). Guarded by <see cref="_admissionGate" />.
    /// </summary>
    private bool _shuttingDown;

    /// <summary>
    ///     Open operations that have been admitted and not yet finished. Shutdown waits for these before
    ///     it snapshots participants: an opener holding a lease may still be constructing a tab, adding
    ///     it, or — if it lost the race — retiring it, and none of those may outlive the shutdown that
    ///     let it in (PRD-005 re-review round 8, finding 1). Guarded by <see cref="_admissionGate" />.
    /// </summary>
    private int _openers;

    private TaskCompletionSource? _openersIdle;

    /// <summary>
    ///     Whether a session restore was started, and whether it finished. Persisting a snapshot taken
    ///     while a restore has not completed would replace the durable session with one that does not
    ///     describe it — the tabs are on disk precisely because they have not been re-opened yet
    ///     (PRD-005 re-review round 10, finding 1). Guarded by <see cref="_admissionGate" />.
    /// </summary>
    private bool _restoreAttempted;

    private bool _restoreCompleted;

    /// <summary>
    ///     Closed at the same moment as document admission. A new debounce or a direct
    ///     <see cref="FlushSessionAsync" /> must not be able to join between the coordinator observing
    ///     session writes as quiescent and starting the final one (PRD-005 re-review round 12,
    ///     finding 1). Guarded by <see cref="_admissionGate" />.
    /// </summary>
    private bool _sessionWritesClosed;

    /// <summary>
    ///     Session writes admitted and not yet finished. The closed flag alone is a check followed by an
    ///     unowned start: an admitted caller is already inside <c>ISessionStore.SaveAsync</c> —
    ///     `JsonSessionStore` creates its directory and temp file before the first suspension — while its
    ///     task does not yet exist to enrol, so the coordinator could observe `_sessionWork` as quiescent
    ///     and start a second writer against the same path (PRD-005 re-review round 13, finding 1).
    ///     Guarded by <see cref="_admissionGate" />.
    /// </summary>
    private int _sessionWriters;

    private TaskCompletionSource? _sessionWritersIdle;

    /// <summary>
    ///     The debounced session persists this view model has started. Shutdown waits for them with the
    ///     tabs: they write through the container-owned session store (PRD-005 re-review round 3).
    /// </summary>
    private readonly BackgroundWorkSet _work = new();

    /// <summary>
    ///     Session writes specifically. They need ordering of their own: a debounced save that has already
    ///     passed its delay is <em>admitted</em> — cancelling its token does not unwind it — so the final
    ///     snapshot must wait for it or it can be overwritten by an older one landing afterwards
    ///     (PRD-005 re-review round 11, finding 1). Both writers target the same temp path, so overlapping
    ///     them is not merely a lost update but an I/O race.
    /// </summary>
    private readonly BackgroundWorkSet _sessionWork = new();

    [ObservableProperty]
    public partial DocumentViewModel? SelectedDocument { get; set; }

    public DocumentsViewModel(
        IDescriptorService descriptors,
        IUiDispatcher dispatcher,
        IClipboardService clipboard,
        IInvocationRunner invocation,
        IDialogService dialogs,
        ILauncherService launcher,
        IRequestValidator validator,
        ISettingsStore settings,
        IThemeService theme,
        IProtocService? protoc = null,
        IFilePickerService? filePicker = null,
        IRevealGate? revealGate = null,
        ConsoleViewModel? console = null,
        IInspector? inspector = null,
        IHistoryRecorder? recorder = null,
        IHistoryStore? history = null,
        IWorkspaceStore? workspace = null,
        ISecretStore? secrets = null,
        ISavedRequestStore? savedRequests = null,
        IEnvironmentService? environment = null,
        IUpdateService? updates = null,
        IDiagnosticsLog? diagnostics = null,
        ISessionStore? session = null,
        IGraphQlService? graphql = null,
        TimeSpan? sessionDebounce = null)
    {
        _descriptors = descriptors;
        _dispatcher = dispatcher;
        _clipboard = clipboard;
        _invocation = invocation;
        _graphql = graphql;
        _dialogs = dialogs;
        _launcher = launcher;
        _validator = validator;
        _settings = settings;
        _theme = theme;
        _protoc = protoc;
        _filePicker = filePicker;
        _revealGate = revealGate;
        _console = console;
        _inspector = inspector;
        _recorder = recorder;
        _history = history;
        _workspace = workspace;
        _secrets = secrets;
        _savedRequests = savedRequests;
        _environment = environment;
        _updates = updates;
        _diagnostics = diagnostics;
        _session = session;
        _sessionDebounce = sessionDebounce ?? TimeSpan.FromSeconds(1);

        // FR-146: any change to the open tabs (or which is active) re-snapshots the session, debounced.
        // BuildSession reads every tab's live draft, so switching/closing tabs persists their current bodies.
        Documents.CollectionChanged += (_, _) => SchedulePersist();
    }

    public ObservableCollection<DocumentViewModel> Documents { get; } = [];

    public void OpenDescribe(SavedConnection connection, string symbol, bool newTab = false)
    {
        // PRD-005: the lease covers the whole operation, so shutdown cannot finish underneath it.
        if (!BeginOpen())
        {
            return;
        }

        try
        {
            if (!newTab)
            {
                var existing = Documents
                    .OfType<DescribeDocumentViewModel>()
                    .FirstOrDefault(d => d.Connection.Id == connection.Id && d.CurrentSymbol == symbol);

                if (existing is not null)
                {
                    SelectedDocument = existing;
                    return;
                }
            }

            var document = new DescribeDocumentViewModel(connection, symbol, _descriptors, _dispatcher, _clipboard, this);

            AdmitAndAdd(document);
        }
        finally
        {
            EndOpen();
        }
    }

    public void OpenInvocation(SavedConnection connection, string methodSymbol, string? initialRequestJson = null)
    {
        // PRD-005: the lease covers the whole operation, so shutdown cannot finish underneath it.
        if (!BeginOpen())
        {
            return;
        }

        try
        {
            var document = CreateInvocationTab(connection, methodSymbol, initialRequestJson);

            // FR-153 / FR-163: seed new tabs from the Network/General defaults (initial values only).
            var network = _settings.Current.Network;

            if (!string.IsNullOrWhiteSpace(network.DefaultDeadline))
            {
                document.Deadline = network.DefaultDeadline;
            }

            if (!string.IsNullOrWhiteSpace(network.MaxMessageSize))
            {
                document.MaxMessageSize = network.MaxMessageSize;
            }

            Finish(document);
        }
        finally
        {
            EndOpen();
        }
    }

    public void OpenInvocation(SavedConnection connection, string methodSymbol, RequestPrefill prefill)
    {
        // PRD-005: the lease covers the whole operation, so shutdown cannot finish underneath it.
        if (!BeginOpen())
        {
            return;
        }

        try
        {
            // FR-123 replay: a plain draft pre-filled from the prefill (no saved-request binding).
            var document = CreateInvocationTab(connection, methodSymbol, prefill.Body);
            ApplyPrefill(document, prefill);
            Finish(document);
        }
        finally
        {
            EndOpen();
        }
    }

    public void OpenSavedRequest(SavedConnection connection, SavedRequest request)
    {
        // PRD-005: the lease covers the whole operation, so shutdown cannot finish underneath it.
        if (!BeginOpen())
        {
            return;
        }

        try
        {
            var document = CreateInvocationTab(connection, request.Method, request.Body);
            ApplyPrefill(document, PrefillFrom(request));

            // FR-002: bind the tab to the saved request and snapshot the baseline (body settles to request.Body).
            document.BindSavedRequest(request.Id, request.Name, request.Body);

            Finish(document);
        }
        finally
        {
            EndOpen();
        }
    }

    private InvocationDocumentViewModel CreateInvocationTab(SavedConnection connection, string method, string? body)
    {
        return new InvocationDocumentViewModel(
            connection, method, body, _invocation, _descriptors, _dispatcher, _clipboard, _dialogs, _launcher, _validator,
            _filePicker, _settings.Current.Network.RingBufferSize, revealGate: _revealGate, documentHost: this,
            console: _console, inspector: _inspector, recorder: _recorder, savedRequests: _savedRequests,
            environment: _environment, tlsProfile: ResolveTlsProfile(connection))
        {
            CliDialect = _settings.Current.General.CliShellDialect
        };
    }

    /// <summary>
    ///     Resolves a TLS connection's referenced profile (FR-012) from the live workspace, for
    ///     copy-as-CLI TLS-flag rendering (B4). Returns null for plaintext connections, connections with
    ///     no profile reference, or when no workspace is wired (e.g. in isolated tests).
    /// </summary>
    private TlsProfile? ResolveTlsProfile(SavedConnection connection)
        => connection is { Transport: TransportMode.Tls, TlsProfileId: { } id }
            ? _workspace?.Current.TlsProfiles.FirstOrDefault(p => p.Id == id)
            : null;

    private static void ApplyPrefill(InvocationDocumentViewModel document, RequestPrefill prefill)
    {
        if (!string.IsNullOrWhiteSpace(prefill.Title))
        {
            document.Title = prefill.Title;
        }

        document.BodyFormat = prefill.BodyFormat;
        document.EmitDefaults = prefill.EmitDefaults;
        document.AllowUnknownFields = prefill.AllowUnknownFields;

        if (!string.IsNullOrWhiteSpace(prefill.Deadline))
        {
            document.Deadline = prefill.Deadline;
        }

        if (!string.IsNullOrWhiteSpace(prefill.MaxMessageSize))
        {
            document.MaxMessageSize = prefill.MaxMessageSize;
        }

        document.Headers.Clear();

        foreach (var header in prefill.Headers)
        {
            document.Headers.Add(new HeaderRowViewModel(
                new HeaderEntry { Name = header.Name, Value = header.Value, IsBin = header.IsBin })
            {
                RequiresValue = header.RequiresValue
            });
        }
    }

    private static RequestPrefill PrefillFrom(SavedRequest request) => new(
        request.Body,
        request.BodyFormat,
        request.Headers.Select(h => new PrefillHeader(h.Name, h.Value, h.IsBin)).ToList(),
        request.Deadline,
        request.EmitDefaults,
        request.AllowUnknownFields,
        (request.MaxReceiveBytes ?? request.MaxSendBytes)?.ToString(CultureInfo.InvariantCulture),
        request.Name);

    private void Finish(DocumentViewModel document)
    {
        AdmitAndAdd(document);
    }

    /// <summary>
    ///     Takes an opener lease, or refuses because shutdown has closed admission (PRD-005 re-review
    ///     round 8, finding 1).
    ///     <para>
    ///         The lease covers the <em>whole</em> open operation, not just its collection commit. Round
    ///         7 linearised the commit, which stopped a tab joining <see cref="Documents" /> late — but a
    ///         refused opener then constructed a tab and disposed it outside anything shutdown waited
    ///         for, so shutdown could report success while that cleanup was still touching singletons.
    ///         Refusing before the tab exists removes the work rather than tidying up after it.
    ///     </para>
    /// </summary>
    private bool BeginOpen()
    {
        lock (_admissionGate)
        {
            if (_shuttingDown)
            {
                return false;
            }

            _openers++;

            return true;
        }
    }

    /// <summary>Releases an opener lease, waking a shutdown that is waiting for the last one.</summary>
    private void EndOpen()
    {
        TaskCompletionSource? idle = null;

        lock (_admissionGate)
        {
            if (--_openers == 0)
            {
                idle = _openersIdle;
                _openersIdle = null;
            }
        }

        _ = idle?.TrySetResult();
    }

    /// <summary>
    ///     One consistent view of what a drain round has to wait for: the documents present, whether any
    ///     opener holds a lease, and the task that completes when the last one lets go.
    ///     <para>
    ///         Taken together under <see cref="_admissionGate" /> because reading them separately is what
    ///         made a round lie. A round would snapshot zero documents while an opener was pending, build
    ///         its wait list, and then evaluate <c>TrueForAll(IsCompleted)</c> a moment later — by which
    ///         time the opener had added its tab and completed its lease. The now-completed lease task
    ///         was read as proof that the <em>earlier</em> empty snapshot had been quiescent, and the
    ///         drain returned without ever rediscovering the tab (PRD-005 re-review round 8).
    ///     </para>
    /// </summary>
    private (DocumentViewModel[] Present, bool OpenersPending, Task Settled) SnapshotForRound()
    {
        lock (_admissionGate)
        {
            var settled = _openers == 0
                ? Task.CompletedTask
                : (_openersIdle ??= new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously)).Task;

            return ([.. Documents], _openers > 0, settled);
        }
    }

    /// <summary>
    ///     Commits a newly built tab to <see cref="Documents" />, or retires it if shutdown won the race
    ///     after all. Reached only while its caller holds an opener lease, which is where admission is
    ///     normally decided (see <see cref="BeginOpen" />) — the re-check here covers the one case the
    ///     lease cannot, an opener slower than the bounded drain.
    ///     <para>
    ///         The add still happens under <see cref="_admissionGate" />, shared with the assignment of
    ///         <see cref="_shuttingDown" />: shutdown must not be able to snapshot participants halfway
    ///         through this. Round 6 checked a plain flag and let each caller add afterwards, which let
    ///         a tab join the collection after shutdown had returned; round 7 linearised the two; round 8
    ///         moved the decision itself out to the lease, so a tab that would lose is never built.
    ///     </para>
    /// </summary>
    private void AdmitAndAdd(DocumentViewModel document)
    {
        bool refused;

        lock (_admissionGate)
        {
            // Re-checked, not assumed. The lease normally keeps shutdown waiting, but the drain is
            // bounded: an opener slower than the timeout can still arrive here after shutdown has given
            // up and returned, and adding a live tab then is the very thing this all exists to prevent.
            refused = _shuttingDown;

            if (!refused)
            {
                document.CloseRequested += OnDocumentCloseRequested;

                Documents.Add(document);
            }
        }

        if (refused)
        {
            // Still inside the lease, so a shutdown that is merely slow — rather than timed out — waits
            // for this. Retire is outside the gate because it drains and disposes.
            Retire(document);

            return;
        }

        // Selection is outside the critical section on purpose: it only drives the UI, and the property
        // change it raises reaches enough of the view model that holding a lock across it is a hazard.
        SelectedDocument = document;
    }

    public void OpenGraphQl(SavedConnection connection)
    {
        // PRD-005: the lease covers the whole operation, so shutdown cannot finish underneath it.
        if (!BeginOpen())
        {
            return;
        }

        try
        {
            if (_graphql is null)
            {
                return; // GraphQL service not wired (bare unit construction)
            }

            var document = new GraphQlDocumentViewModel(
                connection, _graphql, _dispatcher, _clipboard, _recorder, _environment, _filePicker, ResolveTlsProfile(connection), this)
            {
                CliDialect = _settings.Current.General.CliShellDialect
            };
            Finish(document);
        }
        finally
        {
            EndOpen();
        }
    }

    /// <summary>FR-146: restores a GraphQL tab from its session draft (document, variables, options, headers).</summary>
    private void OpenGraphQlDraft(SavedConnection connection, SessionTab tab)
    {
        if (_graphql is null)
        {
            return;
        }

        var document = new GraphQlDocumentViewModel(
            connection, _graphql, _dispatcher, _clipboard, _recorder, _environment, _filePicker, ResolveTlsProfile(connection), this)
        {
            CliDialect = _settings.Current.General.CliShellDialect,
            Document = tab.GraphQlDocument ?? string.Empty,
            VariablesJson = tab.VariablesJson ?? string.Empty,
            DefaultService = tab.DefaultService ?? string.Empty,
            EmitDefaults = tab.EmitDefaults,
            AllowUnknownFields = tab.AllowUnknownFields,
            StrictSelection = tab.StrictSelection,
            Introspection = tab.Introspection,
            Raw = tab.Raw
        };

        foreach (var header in tab.Headers ?? [])
        {
            document.Headers.Add(new HeaderRowViewModel(
                new HeaderEntry { Name = header.Name, Value = header.Value, IsBin = header.IsBin })
            {
                RequiresValue = header.RequiresValue
            });
        }

        document.ReparseAndSelect(tab.OperationName);
        Finish(document);
    }

    public void OpenSettings()
    {
        // PRD-005: the lease covers the whole operation, so shutdown cannot finish underneath it.
        if (!BeginOpen())
        {
            return;
        }

        try
        {
            var existing = Documents.OfType<SettingsDocumentViewModel>().FirstOrDefault();

            if (existing is not null)
            {
                SelectedDocument = existing;
                return;
            }

            var document = new SettingsDocumentViewModel(
                _settings, _theme, _dialogs, _protoc, _secrets, _updates, _launcher, _diagnostics, _clipboard);
            AdmitAndAdd(document);
        }
        finally
        {
            EndOpen();
        }
    }

    public void OpenHistory()
    {
        // PRD-005: the lease covers the whole operation, so shutdown cannot finish underneath it.
        if (!BeginOpen())
        {
            return;
        }

        try
        {
            if (_history is null || _workspace is null)
            {
                return; // history services not wired (bare unit construction)
            }

            var existing = Documents.OfType<HistoryDocumentViewModel>().FirstOrDefault();

            if (existing is not null)
            {
                SelectedDocument = existing;
                return;
            }

            var document = new HistoryDocumentViewModel(_history, _settings, _workspace, this, _dialogs, _dispatcher, _filePicker, _console);
            AdmitAndAdd(document);
        }
        finally
        {
            EndOpen();
        }
    }

    /// <summary>E3.1: closes every open tab (e.g. when switching to a different workspace).</summary>
    public void CloseAll()
    {
        foreach (var document in Documents)
        {
            document.CloseRequested -= OnDocumentCloseRequested;

            // PRD-005: a closed tab is finished, and some of them hold resources that outlive it —
            // debounce token sources, a capture writer, and subscriptions to container singletons that
            // would otherwise root the tab for the life of the process.
            Retire(document);
        }

        Documents.Clear();
        SelectedDocument = null;
    }

    private void OnDocumentCloseRequested(object? sender, EventArgs e)
    {
        if (sender is not DocumentViewModel document)
        {
            return;
        }

        document.CloseRequested -= OnDocumentCloseRequested;

        var index = Documents.IndexOf(document);
        _ = Documents.Remove(document);

        if (SelectedDocument == document)
        {
            SelectedDocument = Documents.Count == 0
                ? null
                : Documents[Math.Min(index, Documents.Count - 1)];
        }

        // PRD-005. After the removal and the selection move, so nothing observing either touches a
        // disposed tab: SchedulePersist runs off the Documents collection, which no longer holds it.
        Retire(document);
    }

    partial void OnSelectedDocumentChanged(DocumentViewModel? value) => SchedulePersist();

    // ── SPEC-020 §5: shell keyboard actions routed to the active tab ──────────
    // The shell binds these to window-level KeyBindings so they fire even while an AvaloniaEdit
    // editor holds focus; each is a safe no-op when there is no tab (or no applicable action).

    /// <summary>Ctrl+Tab / Ctrl+PageDown: activate the next tab, wrapping at the end.</summary>
    [RelayCommand]
    private void SelectNextDocument() => CycleSelection(1);

    /// <summary>Ctrl+Shift+Tab / Ctrl+PageUp: activate the previous tab, wrapping at the start.</summary>
    [RelayCommand]
    private void SelectPreviousDocument() => CycleSelection(-1);

    private void CycleSelection(int delta)
    {
        if (Documents.Count == 0)
        {
            return;
        }

        var current = SelectedDocument is null ? 0 : Documents.IndexOf(SelectedDocument);
        var next = ((current + delta) % Documents.Count + Documents.Count) % Documents.Count;
        SelectedDocument = Documents[next];
    }

    /// <summary>Ctrl+W: close the active tab. Routes through the tab's own close path, so a derived
    /// document's live-stream confirmation (FR — close while streaming) still runs.</summary>
    [RelayCommand]
    private void CloseActiveDocument() => SelectedDocument?.CloseCommand.Execute(null);

    /// <summary>Ctrl+Enter: run the active tab's primary action — invoke (unary), start (streaming),
    /// or execute (GraphQL). No-op for non-runnable tabs (settings, history, describe).</summary>
    [RelayCommand]
    private void RunActiveDocument()
    {
        switch (SelectedDocument)
        {
            case InvocationDocumentViewModel invocation:
                Execute(invocation.IsStreaming ? invocation.StartStreamCommand : invocation.InvokeCommand);
                break;
            case GraphQlDocumentViewModel graphql:
                Execute(graphql.ExecuteCommand);
                break;
        }
    }

    /// <summary>Ctrl+. : cancel the active tab's in-flight call or stream. No-op when nothing is running.</summary>
    [RelayCommand]
    private void CancelActiveDocument()
    {
        switch (SelectedDocument)
        {
            case InvocationDocumentViewModel invocation:
                Execute(invocation.IsStreaming ? invocation.StartStreamCancelCommand : invocation.InvokeCancelCommand);
                break;
            case GraphQlDocumentViewModel graphql:
                Execute(graphql.ExecuteCancelCommand);
                break;
        }
    }

    /// <summary>Ctrl+Shift+F: format the active tab's editable JSON (request body / GraphQL variables).
    /// No-op for tabs without a formattable editor (settings, history, describe).</summary>
    [RelayCommand]
    private void FormatActiveDocument()
    {
        switch (SelectedDocument)
        {
            case InvocationDocumentViewModel invocation:
                Execute(invocation.FormatCommand);
                break;
            case GraphQlDocumentViewModel graphql:
                Execute(graphql.FormatCommand);
                break;
        }
    }

    private static void Execute(System.Windows.Input.ICommand command)
    {
        if (command.CanExecute(null))
        {
            command.Execute(null);
        }
    }

    // ── FR-146: UI session capture + restore ─────────────────────────────────

    /// <summary>Snapshots the open invocation/describe tabs (with their live drafts) for the active workspace.</summary>
    public SessionState BuildSession()
    {
        var state = new SessionState
        {
            WorkspaceId = _workspace?.Current.Id,
            ActiveTabIndex = SelectedDocument is null ? -1 : Documents.IndexOf(SelectedDocument)
        };

        foreach (var document in Documents)
        {
            switch (document)
            {
                case InvocationDocumentViewModel invocation:
                    state.Tabs.Add(new SessionTab(
                        SessionTabKind.Invocation, invocation.Connection.Id, invocation.MethodSymbol,
                        invocation.RequestJson, invocation.BodyFormat,
                        invocation.Headers
                            .Select(ToSessionHeader).ToList(),
                        NullIfEmpty(invocation.Deadline), invocation.EmitDefaults, invocation.AllowUnknownFields,
                        NullIfEmpty(invocation.MaxMessageSize)));
                    break;

                case DescribeDocumentViewModel describe:
                    state.Tabs.Add(new SessionTab(SessionTabKind.Describe, describe.Connection.Id, describe.CurrentSymbol));
                    break;

                case GraphQlDocumentViewModel graphql:
                    state.Tabs.Add(new SessionTab(
                        SessionTabKind.GraphQl, graphql.Connection.Id, Symbol: string.Empty,
                        Headers: graphql.Headers.Select(ToSessionHeader).ToList(),
                        EmitDefaults: graphql.EmitDefaults,
                        AllowUnknownFields: graphql.AllowUnknownFields,
                        GraphQlDocument: graphql.Document,
                        OperationName: graphql.SelectedOperation?.Name,
                        VariablesJson: NullIfEmpty(graphql.VariablesJson),
                        DefaultService: NullIfEmpty(graphql.DefaultService),
                        StrictSelection: graphql.StrictSelection,
                        Introspection: graphql.Introspection,
                        Raw: graphql.Raw));
                    break;
            }
        }

        return state;
    }

    /// <summary>
    ///     Projects a live header row into its persisted session form, redacting secrets first
    ///     (B1 — security). A sensitive-named header carrying a literal value (per the same rule
    ///     the workspace save-guard enforces) is persisted with an empty value and
    ///     <c>RequiresValue=true</c>, so the secret bytes never reach <c>ui-state.json</c> and the
    ///     restored tab prompts the user to re-enter it (FR-123). <c>${VAR}</c>-referencing and
    ///     non-sensitive headers are persisted verbatim.
    /// </summary>
    private static SessionHeader ToSessionHeader(HeaderRowViewModel header)
        => WorkspaceSecretScanner.IsSensitiveHeaderLiteral(header.Name, header.Value)
            ? new SessionHeader(header.Name, string.Empty, header.IsBin, RequiresValue: true)
            : new SessionHeader(header.Name, header.Value, header.IsBin, header.RequiresValue);

    /// <summary>
    ///     FR-146/FR-151: restores the prior tabs on launch when the startup setting asks to reopen the last
    ///     workspace and its tabs; a no-op when the setting is "start empty".
    /// </summary>
    public Task RestoreSessionOnStartupAsync(CancellationToken cancellationToken = default)
        => _settings.Current.General.Startup == StartupBehavior.RestoreLastWorkspace
            ? RestoreSessionAsync(cancellationToken)
            : Task.CompletedTask;

    /// <summary>
    ///     Restores the previously open tabs for the current workspace as drafts (run state idle). Tabs whose
    ///     connection no longer exists, or that belong to a different workspace, are skipped.
    /// </summary>
    public async Task RestoreSessionAsync(CancellationToken cancellationToken = default)
    {
        if (_session is null || _workspace is null)
        {
            return;
        }

        // A lease, exactly as the public Open* methods take: restore constructs documents and awaits the
        // singleton session store, so it is an open operation in everything but name (PRD-005 re-review
        // round 9, finding 1). The lease is taken before the first await, so the fire-and-forget call in
        // App holds it by the time that call returns.
        lock (_admissionGate)
        {
            _restoreAttempted = true;
        }

        if (!BeginOpen())
        {
            return; // shutdown started first: load nothing, construct nothing
        }

        try
        {
            await RestoreSessionCoreAsync(cancellationToken).ConfigureAwait(false);

            lock (_admissionGate)
            {
                // "Returned" is not "restored". If shutdown began at any point during the restore, the
                // tabs it was re-opening were refused by admission, so the collection does not describe
                // the session and must not be persisted over it.
                _restoreCompleted = !_shuttingDown;
            }
        }
        finally
        {
            EndOpen();
        }
    }

    private async Task RestoreSessionCoreAsync(CancellationToken cancellationToken)
    {
        var state = await _session!.LoadAsync(cancellationToken).ConfigureAwait(false);

        if (state.WorkspaceId is null || state.WorkspaceId != _workspace!.Current.Id)
        {
            return; // a different (or no) workspace than the one whose tabs were saved
        }

        _suppressPersist = true; // opening the restored tabs shouldn't re-persist the same session

        try
        {
            foreach (var tab in state.Tabs)
            {
                var connection = _workspace.Current.Connections.FirstOrDefault(c => c.Id == tab.ConnectionId);

                if (connection is null)
                {
                    continue;
                }

                switch (tab.Kind)
                {
                    case SessionTabKind.Describe:
                        OpenDescribe(connection, tab.Symbol, newTab: true);
                        break;
                    case SessionTabKind.GraphQl:
                        OpenGraphQlDraft(connection, tab);
                        break;
                    default:
                        OpenInvocation(connection, tab.Symbol, ToPrefill(tab));
                        break;
                }
            }

            if (state.ActiveTabIndex >= 0 && state.ActiveTabIndex < Documents.Count)
            {
                SelectedDocument = Documents[state.ActiveTabIndex];
            }
        }
        finally
        {
            _suppressPersist = false;
        }
    }

    /// <summary>
    ///     Writes the current session immediately, cancelling any pending debounce.
    ///     <para>
    ///         Enrolled in the same sets as every other session write, and refused once shutdown has
    ///         closed session-write admission: this is a public entry point straight to
    ///         <see cref="PersistNowAsync" />, and one that joined mid-shutdown would race the
    ///         coordinator's final write for the same temp path (PRD-005 re-review round 12, finding 1).
    ///     </para>
    /// </summary>
    public async Task FlushSessionAsync(CancellationToken cancellationToken = default)
    {
        if (!BeginSessionWrite())
        {
            return;
        }

        try
        {
            CancelPendingPersist();

            var save = PersistNowAsync(cancellationToken);

            _sessionWork.Track(save);
            _work.Track(save);

            await save.ConfigureAwait(false);
        }
        finally
        {
            EndSessionWrite();
        }
    }

    /// <summary>
    ///     The whole document half of application shutdown, in the order the phases actually depend on
    ///     each other (PRD-005 re-review round 10, finding 1).
    ///     <list type="number">
    ///         <item>close admission and wait for document producers already in flight;</item>
    ///         <item>persist the session — but only if it can describe the user's tabs;</item>
    ///         <item>cancel, drain and dispose the documents.</item>
    ///     </list>
    ///     <para>
    ///         <c>Program</c> used to flush first and drain second. A rapid close during startup restore
    ///         therefore snapshotted an empty <see cref="Documents" /> — the restore was still parked in
    ///         the session store — and wrote it over the very file being restored. The tabs were lost by
    ///         the shutdown that was supposed to preserve them.
    ///     </para>
    ///     <para>
    ///         Persistence is <b>skipped</b> rather than approximated when a restore was attempted and did
    ///         not finish: the durable file is already the truth, and replacing it with a snapshot known
    ///         to be incomplete is the one outcome worse than not writing. The caller is told, through
    ///         <see cref="DocumentShutdownResult.SessionPersisted" />.
    ///     </para>
    /// </summary>
    /// <param name="budget">
    ///     The whole document-shutdown budget, shared by all three phases: settling producers, persisting
    ///     the session, and draining the tabs. It is a ceiling on the coordinator, not on each phase — a
    ///     stalled session write cannot extend it, and a persistence timeout or failure is reported through
    ///     <see cref="DocumentShutdownResult.SessionPersisted" /> rather than thrown, so the disposal phase
    ///     still runs.
    /// </param>
    public async Task<DocumentShutdownResult> ShutdownAsync(TimeSpan budget)
    {
        var clock = Stopwatch.StartNew();

        CloseAdmission();

        // Phase 1: let producers already in flight finish, so the snapshot below describes a settled
        // collection rather than one mid-restore.
        var producersSettled = true;

        try
        {
            await SnapshotForRound().Settled.WaitAsync(budget).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            producersSettled = false;
        }

        // Phase 2: persist only what can be described truthfully, and only once the writes already in
        // flight have landed.
        bool restoreUsable;

        lock (_admissionGate)
        {
            restoreUsable = !_restoreAttempted || _restoreCompleted;
        }

        var sessionPersisted = producersSettled
            && restoreUsable
            && await PersistFinalSessionAsync(budget, clock).ConfigureAwait(false);

        // Phase 3 runs whatever happened above: a persistence failure must not cost the tabs their
        // disposal, the workspace its lock release, or the host its StopAsync.
        var result = await DisposeOpenDocumentsAsync(Remaining(budget, clock)).ConfigureAwait(false);

        return result with { SessionPersisted = sessionPersisted };
    }

    /// <summary>
    ///     Requests cancellation without letting the request itself break shutdown. A registered callback
    ///     may throw — <see cref="CancellationTokenSource.Cancel()" /> then surfaces an
    ///     <see cref="AggregateException" /> — and a source disposed by a save completing at the same
    ///     instant yields <see cref="ObjectDisposedException" />. Neither is worth the disposal phase.
    /// </summary>
    private static void RequestCancelQuietly(CancellationTokenSource source)
    {
        try
        {
            source.Cancel();
        }
        catch (Exception)
        {
            // Best effort: the task stays tracked either way, so the drain still reports it.
        }
    }

    /// <summary>
    ///     Asks the pending debounced write to stop. Every internal cancellation request goes through here,
    ///     so none of them can break its caller — a store's registered callback may throw — and so each one
    ///     is counted as an owner of the source for as long as it is running.
    /// </summary>
    private void CancelPendingPersist()
    {
        // Read once: the field is replaced by SchedulePersist and cleared by shutdown.
        _persistCancellation?.RequestCancel();
    }

    private static TimeSpan Remaining(TimeSpan budget, Stopwatch clock)
    {
        var left = budget - clock.Elapsed;

        return left > TimeSpan.Zero ? left : TimeSpan.Zero;
    }

    /// <summary>
    ///     Writes the final session snapshot, after every session write already admitted has finished.
    ///     Returns whether the durable session now describes this shutdown.
    ///     <para>
    ///         The wait is the point. Cancelling a debounce's token does not unwind a save that has passed
    ///         its delay and is already inside the store, so starting the final write immediately let the
    ///         older one land afterwards and replace it — with <c>SessionPersisted: true</c> reported
    ///         either way (round 11, finding 1). Both writers use the same temp path, so overlapping them
    ///         risks an I/O failure as much as a lost update.
    ///     </para>
    ///     <para>
    ///         Bounded and contained (round 11, finding 2). If an earlier writer does not settle in time,
    ///         no second writer is started at all; if the final write stalls or throws, that is reported
    ///         rather than propagated, because the phase after this one is what disposes the tabs.
    ///     </para>
    /// </summary>
    private async Task<bool> PersistFinalSessionAsync(TimeSpan budget, Stopwatch clock)
    {
        if (_session is null)
        {
            return false; // nothing to persist through; see DocumentShutdownResult.SessionPersisted
        }

        // Contained, and for the same reason the timeout's request below is: this runs the pending
        // debounce's registered callbacks on the shutdown thread, and round 13 fixed that only for the
        // final save's own source. A throwing callback here escaped before either wait, so shutdown
        // never reached the lease waits, the final write, or phase 3 (round 14, finding 2).
        CancelPendingPersist();

        // Both waits measure against the coordinator's one clock. Giving each the whole remaining
        // interval let persistence spend the budget twice and the coordinator overrun the ceiling its
        // documentation promises (round 12, finding 2).
        try
        {
            // Both: the lease covers a writer inside the store whose task is not yet enrolled, and the
            // work set covers one whose task exists.
            await SessionWritersSettled().WaitAsync(Remaining(budget, clock)).ConfigureAwait(false);
            await _sessionWork.WhenSettled().WaitAsync(Remaining(budget, clock)).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            return false; // an earlier writer is still going: do not race it for the same file
        }

        // Started as a tracked task with a real token. Round 11 wrapped the call in WaitAsync and left it
        // enrolled in nothing, so a timed-out save kept writing where no drain could see it and shutdown
        // reported Drained: true over it — then Program released the lock and called Environment.Exit(0)
        // part-way through the temp-file protocol (round 12, finding 1). Stopping the wait is not
        // completing the work.
        var cancellation = new CancellationTokenSource();
        var save = PersistNowAsync(cancellation.Token);

        _sessionWork.Track(save);
        _work.Track(save);

        try
        {
            await save.WaitAsync(Remaining(budget, clock)).ConfigureAwait(false);

            // Completed: nothing else can touch the source, so dispose it here rather than racing a
            // continuation against the timeout path (round 13, finding 2).
            cancellation.Dispose();

            return true;
        }
        catch (TimeoutException)
        {
            // Ask it to stop, but keep it tracked: the drain must be able to say it is still running.
            // Contained, because Cancel() runs the store's registered callbacks on this thread and an
            // AggregateException from one of them would skip phase 3 entirely — the interrupted-cleanup
            // class this PR exists to remove.
            RequestCancelQuietly(cancellation);

            // Disposed only once the save actually settles, so Dispose cannot race the Cancel above.
            _ = save.ContinueWith(
                static (_, state) => ((CancellationTokenSource)state!).Dispose(),
                cancellation,
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);

            return false;
        }
        catch (Exception)
        {
            // A failed session write is not worth abandoning the rest of shutdown for. The save is done,
            // so the source is safe to release here.
            cancellation.Dispose();

            return false;
        }
    }

    /// <summary>Closes admission. Idempotent; <see cref="DisposeOpenDocumentsAsync" /> also calls it.</summary>
    private void CloseAdmission()
    {
        lock (_admissionGate)
        {
            _shuttingDown = true;
            _sessionWritesClosed = true;
        }
    }

    /// <summary>
    ///     Takes a session-writer lease, or refuses because shutdown has closed session-write admission.
    ///     Taken <b>before</b> any store code runs, so admission and ownership are one critical section.
    /// </summary>
    private bool BeginSessionWrite()
    {
        lock (_admissionGate)
        {
            if (_sessionWritesClosed)
            {
                return false;
            }

            _sessionWriters++;

            return true;
        }
    }

    private void EndSessionWrite()
    {
        TaskCompletionSource? idle = null;

        lock (_admissionGate)
        {
            if (--_sessionWriters == 0)
            {
                idle = _sessionWritersIdle;
                _sessionWritersIdle = null;
            }
        }

        _ = idle?.TrySetResult();
    }

    /// <summary>Completes when no admitted session writer is still inside the store.</summary>
    private Task SessionWritersSettled()
    {
        lock (_admissionGate)
        {
            return _sessionWriters == 0
                ? Task.CompletedTask
                : (_sessionWritersIdle ??= new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously)).Task;
        }
    }

    /// <summary>
    ///     Stops and disposes every open tab at application shutdown, after the final session snapshot
    ///     has been taken (PRD-005 review, finding 4).
    ///     <para>
    ///         The disposal wired into the close flow only runs for a tab the user actually closes, so
    ///         exiting Studio with tabs open bypassed all of it: in-flight calls, debounce work, capture
    ///         writers and singleton subscriptions survived until the forced process exit instead of
    ///         being released deterministically.
    ///     </para>
    ///     <para>
    ///         Two phases, because cancellation is a request and not a completion. Every tab is
    ///         cancelled first, then all of them are drained together, and only then are they disposed —
    ///         so the caller can dispose the host afterwards knowing no tab is still reaching for the
    ///         history, validation or secret singletons it is about to tear down (PRD-005 re-review,
    ///         finding 1). Cancelling tab-by-tab-then-waiting would serialise the drain instead.
    ///     </para>
    ///     <para>
    ///         <b>Every</b> tab is drained, not only the ones with cancellable work, and this view
    ///         model's own debounced persist with them. Round 2 drained a hand-picked subset and so
    ///         could report success while a settings refresh, a history load, a debounced validation or
    ///         a superseded describe lookup was still running against a singleton (round 3, finding 1).
    ///         Cancellation is still selective; waiting is not.
    ///     </para>
    ///     <para>
    ///         Deliberately <b>not</b> <see cref="CloseAll" />. That clears <see cref="Documents" />,
    ///         whose collection-changed handler schedules a persist — which at this point would write an
    ///         empty session over the snapshot just taken. This leaves the collection alone: the process
    ///         is going away, and the session on disk must keep describing the tabs that were open.
    ///     </para>
    /// </summary>
    /// <param name="drainTimeout">
    ///     How long to wait for cancelled work to unwind. Bounded because shutdown must terminate even
    ///     if an operation ignores its token; a timeout is reported rather than hidden, because "we
    ///     stopped waiting" is not the same claim as "nothing is running".
    /// </param>
    public async Task<DocumentShutdownResult> DisposeOpenDocumentsAsync(TimeSpan drainTimeout)
    {
        // Both set before any await. The flag goes under the admission gate so it linearises against a
        // commit already in flight: no tab joins Documents after this point, and none is lost either.
        CloseAdmission();

        _suppressPersist = true;

        // Cancelled, but not disposed here: an admitted debounce can still be inside the store holding
        // this token, and destroying the source underneath it is the same ownership error as abandoning
        // a task (round 14, finding 2). Whichever of the write and the cancellation request finishes last
        // releases it.
        CancelPendingPersist();

        _persistCancellation = null;

        // Grows as the drain runs: work that is already admitted can open a tab (History's replay does
        // exactly that), and a fixed snapshot would neither wait for it nor dispose it (PRD-005
        // re-review round 5, finding 1). Every document ever observed stays a disposal target.
        var participants = new List<DocumentViewModel>();

        var drained = await DrainToQuiescenceAsync(participants, drainTimeout).ConfigureAwait(false);

        foreach (var document in participants)
        {
            DisposeQuietly(document);
        }

        return new DocumentShutdownResult(participants.Count, drained);
    }

    /// <summary>
    ///     Cancels and waits until nothing is outstanding anywhere — the open tabs, the tabs the user
    ///     closed earlier, and this view model's own debounced persist — or until the budget runs out.
    ///     <para>
    ///         A single pass is not enough, even though each tab's own drain waits for quiescence
    ///         <em>within</em> that tab. Work crosses the boundary: a task on one tab can open another
    ///         through <see cref="IDocumentHost" />, and a retired tab's drain can complete before an
    ///         open tab spawns more. So each round re-reads <see cref="Documents" /> as well as
    ///         re-asking the participants already known, and the loop ends only on a round that both
    ///         found no new tab and saw every participant report nothing outstanding — which
    ///         <c>CancelAndDrainAsync</c> signals by returning an already-completed task.
    ///     </para>
    ///     <para>
    ///         <paramref name="participants" /> is an out-parameter as much as an input: it accumulates
    ///         every document observed in any round, and the caller disposes exactly that set. A tab
    ///         opened mid-drain would otherwise be left both undrained and undisposed.
    ///     </para>
    ///     <para>
    ///         Cancellation is re-requested every round and is idempotent. The budget is the caller's
    ///         single timeout, shared across all rounds, so a livelock of work that keeps spawning work
    ///         ends in a reported timeout rather than a hang.
    ///     </para>
    /// </summary>
    private async Task<bool> DrainToQuiescenceAsync(List<DocumentViewModel> participants, TimeSpan budget)
    {
        var clock = Stopwatch.StartNew();

        while (true)
        {
            var discovered = false;

            // One gated read: the documents, whether an opener is mid-flight, and the wait for it.
            var (present, openersPending, settled) = SnapshotForRound();

            foreach (var document in present)
            {
                if (!participants.Contains(document))
                {
                    participants.Add(document);
                    discovered = true;
                }
            }

            // The session-writer lease belongs in *every* round, not only the coordinator's persistence
            // phase (round 14, finding 1). A writer inside the store's synchronous prefix exists in no
            // task set, so without the lease a direct drain — or a coordinator run that skipped phase 2
            // to protect an unfinished restore — reported success straight over it. Unlike the opener
            // lease it needs no rediscovery loop: a session write cannot add a tab, so a lease that
            // completes before the check below leaves nothing undiscovered behind it.
            var round = new List<Task>(participants.Count + 3)
            {
                _work.WhenSettled(), settled, SessionWritersSettled()
            };

            foreach (var document in participants)
            {
                // Cancels synchronously and hands back the wait, so every tab is cancelled before the
                // first await below.
                round.Add(document.CancelAndDrainAsync());
            }

            // Three separate reasons to go round again, and only the first two are about tasks:
            //   - a tab was discovered, whose constructor work may not have reached its set yet;
            //   - an opener was pending *when the snapshot was taken*, so this round's view of the
            //     documents is already known to be incomplete — its lease completing before the check
            //     below proves nothing about the snapshot that preceded it;
            //   - something is still running.
            if (!discovered && !openersPending && round.TrueForAll(task => task.IsCompleted))
            {
                return true;
            }

            var remaining = budget - clock.Elapsed;

            if (remaining <= TimeSpan.Zero)
            {
                return false;
            }

            try
            {
                await Task.WhenAll(round).WaitAsync(remaining).ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
                return false;
            }
        }
    }

    /// <summary>
    ///     Retires a tab the user has closed: cancels its work, keeps the wait for it in this view
    ///     model's own set, then disposes it (PRD-005 re-review round 4, finding 1).
    ///     <para>
    ///         Disposal is not completion. Before this, closing a tab removed the only handle on its
    ///         outstanding work — a Settings refresh mid-flight through the singleton secret store, say —
    ///         so a shutdown moments later saw no documents, reported <c>Drained: true</c>, and disposed
    ///         the provider underneath it. The tab goes away; the obligation to wait for its work does
    ///         not, so it moves to an owner that outlives the tab.
    ///     </para>
    /// </summary>
    private void Retire(DocumentViewModel document)
    {
        _work.Track(document.CancelAndDrainAsync());

        DisposeQuietly(document);
    }

    /// <summary>
    ///     Disposes one tab without letting its failure stop the rest (PRD-005 re-review round 3,
    ///     finding 2).
    ///     <para>
    ///         A capture sink's close can fail for reasons that have nothing to do with Studio — a full
    ///         disk, a removed drive. Letting that propagate out of the shutdown loop left the remaining
    ///         tabs undisposed and skipped the lock release and <c>StopAsync</c> that follow it, which is
    ///         the same interrupted-cleanup class of failure this whole PR exists to remove. The failure
    ///         is recorded rather than silently dropped.
    ///     </para>
    /// </summary>
    private void DisposeQuietly(DocumentViewModel document)
    {
        try
        {
            (document as IDisposable)?.Dispose();
        }
        catch (Exception ex)
        {
            _diagnostics?.Log(
                DiagnosticsLevel.Warning,
                "shutdown",
                $"Disposing the '{document.Title}' tab failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private static RequestPrefill ToPrefill(SessionTab tab) => new(
        tab.Body ?? string.Empty,
        tab.BodyFormat,
        (tab.Headers ?? []).Select(h => new PrefillHeader(h.Name, h.Value, h.IsBin, h.RequiresValue)).ToList(),
        tab.Deadline,
        tab.EmitDefaults,
        tab.AllowUnknownFields,
        tab.MaxMessageSize);

    private static string? NullIfEmpty(string value) => string.IsNullOrWhiteSpace(value) ? null : value;

    private void SchedulePersist()
    {
        // A cheap early-out only: the write itself is admitted under a lease in DelayedPersistAsync,
        // which is where the boundary that matters lives.
        if (_session is null || _suppressPersist)
        {
            return;
        }

        CancelPendingPersist();

        var cancellation = new PersistCancellation();
        _persistCancellation = cancellation;
        var persist = DelayedPersistAsync(cancellation.Token);

        _work.Track(persist);
        _sessionWork.Track(persist);

        // The write is one owner of the source, not the only one, so its completion is reported rather
        // than acted on: PersistCancellation disposes only when the cancellation requests have returned
        // too (round 15, finding 1).
        _ = persist.ContinueWith(
            static (_, state) => ((PersistCancellation)state!).WriteCompleted(),
            cancellation,
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    private async Task DelayedPersistAsync(CancellationToken token)
    {
        try
        {
            await Task.Delay(_sessionDebounce, token).ConfigureAwait(false);

            // The lease is taken here, not when the debounce was scheduled: what must be owned is the
            // interval the write is inside the store (round 13, finding 1).
            if (!BeginSessionWrite())
            {
                return;
            }

            try
            {
                await PersistNowAsync(token).ConfigureAwait(false);
            }
            finally
            {
                EndSessionWrite();
            }
        }
        catch (OperationCanceledException)
        {
            // Superseded by a newer change or an explicit flush.
        }
    }

    private async Task PersistNowAsync(CancellationToken cancellationToken)
    {
        if (_session is null)
        {
            return;
        }

        await _session.SaveAsync(BuildSession(), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    ///     One debounced write's cancellation, with the source released only when <b>both</b> its owners
    ///     have finished with it: the write that holds the token, and every cancellation request that is
    ///     still running its callbacks (PRD-005 re-review round 15, finding 1).
    ///     <para>
    ///         Round 14 gave the source to the write alone. A registered callback is allowed to complete
    ///         the operation's task synchronously, and the release continuation runs
    ///         <see cref="TaskContinuationOptions.ExecuteSynchronously" /> — so completing the write from
    ///         inside a callback disposed the source on that callback's own stack, while the
    ///         <see cref="CancellationTokenSource.Cancel()" /> call that invoked it was still working through
    ///         its registrations. Containing the resulting exception would only hide it; the remaining
    ///         callbacks still lose their cleanup.
    ///     </para>
    ///     <para>
    ///         The lock is deliberately <b>not</b> held across the cancellation call.
    ///         A callback completing the write on the cancelling thread re-enters this type through
    ///         <see cref="WriteCompleted" />, and <see cref="System.Threading.Lock" /> is recursive — so
    ///         holding it across the call would admit exactly the disposal it is meant to prevent, just
    ///         without the exception to show for it. The counter is what makes the request an owner, not
    ///         the lock.
    ///     </para>
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Microsoft.Design",
        "CA1001:TypesThatOwnDisposableFieldsShouldBeDisposable",
        Justification = "The source's lifetime is exactly what this type exists to arbitrate: it is released "
                        + "when the write and every cancellation request have finished with it, and by no one "
                        + "else. A public Dispose would offer callers the third-party disposal that round 15's "
                        + "finding is about.")]
    private sealed class PersistCancellation
    {
        private readonly CancellationTokenSource _source = new();

        private readonly System.Threading.Lock _gate = new();

        /// <summary>Cancellation requests that have not yet returned. Guarded by <see cref="_gate" />.</summary>
        private int _requests;

        private bool _writeCompleted;

        private bool _released;

        /// <summary>
        ///     Captured once, at construction: reading <see cref="CancellationTokenSource.Token" /> after
        ///     the source is released would throw, and the token itself stays usable afterwards.
        /// </summary>
        public CancellationToken Token { get; }

        public PersistCancellation() => Token = _source.Token;

        /// <summary>
        ///     Requests cancellation, owning the source for the duration. Never throws: a store's callback
        ///     may, and this runs on the shutdown thread inside a <c>catch</c>, where an escape skips the
        ///     disposal phase (round 13, finding 2).
        /// </summary>
        public void RequestCancel()
        {
            lock (_gate)
            {
                if (_released)
                {
                    return; // the write finished and no request was outstanding: nothing left to cancel
                }

                _requests++;
            }

            try
            {
                RequestCancelQuietly(_source);
            }
            finally
            {
                ReleaseIfDone(requestReturned: true);
            }
        }

        /// <summary>The write has finished with the token. It may be the last owner, or it may not.</summary>
        public void WriteCompleted() => ReleaseIfDone(writeFinished: true);

        private void ReleaseIfDone(bool requestReturned = false, bool writeFinished = false)
        {
            lock (_gate)
            {
                if (requestReturned)
                {
                    _requests--;
                }

                if (writeFinished)
                {
                    _writeCompleted = true;
                }

                if (_released || !_writeCompleted || _requests > 0)
                {
                    return;
                }

                _released = true;
            }

            // Outside the lock: nothing else can reach the source now that _released is set.
            _source.Dispose();
        }
    }
}
