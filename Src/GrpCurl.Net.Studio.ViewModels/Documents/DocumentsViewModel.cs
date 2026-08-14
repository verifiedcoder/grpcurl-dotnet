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

    private CancellationTokenSource? _persistCts;
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
    ///     The admission gate, for the PRD-005 admission-race test only. Holding it is the only way to
    ///     park an opener and a shutdown on the same boundary and let them race deterministically;
    ///     nothing in production reaches for this.
    /// </summary>
    internal System.Threading.Lock AdmissionGateForTests => _admissionGate;

    /// <summary>
    ///     The debounced session persists this view model has started. Shutdown waits for them with the
    ///     tabs: they write through the container-owned session store (PRD-005 re-review round 3).
    /// </summary>
    private readonly BackgroundWorkSet _work = new();

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

        _ = AdmitAndAdd(document);
    }

    public void OpenInvocation(SavedConnection connection, string methodSymbol, string? initialRequestJson = null)
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

    public void OpenInvocation(SavedConnection connection, string methodSymbol, RequestPrefill prefill)
    {
        // FR-123 replay: a plain draft pre-filled from the prefill (no saved-request binding).
        var document = CreateInvocationTab(connection, methodSymbol, prefill.Body);
        ApplyPrefill(document, prefill);
        Finish(document);
    }

    public void OpenSavedRequest(SavedConnection connection, SavedRequest request)
    {
        var document = CreateInvocationTab(connection, request.Method, request.Body);
        ApplyPrefill(document, PrefillFrom(request));

        // FR-002: bind the tab to the saved request and snapshot the baseline (body settles to request.Body).
        document.BindSavedRequest(request.Id, request.Name, request.Body);

        Finish(document);
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
        _ = AdmitAndAdd(document);
    }

    /// <summary>
    ///     Commits a newly built tab to <see cref="Documents" />, or refuses it because shutdown has
    ///     started (PRD-005 re-review rounds 6 and 7, finding 1).
    ///     <para>
    ///         A refused tab is not simply dropped: it already exists, and its constructor has already
    ///         started work against container singletons. It goes straight to <see cref="Retire" />, so
    ///         it is cancelled, disposed, and its work joins the drain this view model is already
    ///         waiting on — the same treatment a tab the user closed receives.
    ///     </para>
    ///     <para>
    ///         The check and the add are <b>one critical section</b>, shared with the assignment of
    ///         <see cref="_shuttingDown" />. Round 6 checked a plain flag and let each caller add
    ///         afterwards, which left a gap: an opener could pass the check, shutdown could then set the
    ///         flag and complete an empty drain, and the opener could commit a live tab after shutdown
    ///         had returned. Linearising the two leaves exactly two outcomes — committed before shutdown
    ///         takes the gate and therefore in its snapshot, or refused and retired.
    ///     </para>
    ///     <para>
    ///         Discovery inside the drain loop cannot cover this on its own: a round that ends at the
    ///         timeout never runs another discovery pass, so a tab opened during that round would be
    ///         left both undrained and undisposed.
    ///     </para>
    /// </summary>
    /// <returns><see langword="true" /> when the tab was admitted and added.</returns>
    private bool AdmitAndAdd(DocumentViewModel document)
    {
        bool admitted;

        lock (_admissionGate)
        {
            admitted = !_shuttingDown;

            if (admitted)
            {
                document.CloseRequested += OnDocumentCloseRequested;

                Documents.Add(document);
            }
        }

        if (!admitted)
        {
            // Outside the gate: Retire drains and disposes, which must not run under it.
            Retire(document);

            return false;
        }

        // Selection is outside the critical section on purpose: it only drives the UI, and the property
        // change it raises reaches enough of the view model that holding a lock across it is a hazard.
        SelectedDocument = document;

        return true;
    }

    public void OpenGraphQl(SavedConnection connection)
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
        var existing = Documents.OfType<SettingsDocumentViewModel>().FirstOrDefault();

        if (existing is not null)
        {
            SelectedDocument = existing;
            return;
        }

        var document = new SettingsDocumentViewModel(
            _settings, _theme, _dialogs, _protoc, _secrets, _updates, _launcher, _diagnostics, _clipboard);
        _ = AdmitAndAdd(document);
    }

    public void OpenHistory()
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
        _ = AdmitAndAdd(document);
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

        var state = await _session.LoadAsync(cancellationToken).ConfigureAwait(false);

        if (state.WorkspaceId is null || state.WorkspaceId != _workspace.Current.Id)
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

    /// <summary>Writes the current session immediately (cancelling any pending debounce); used on shutdown.</summary>
    public async Task FlushSessionAsync(CancellationToken cancellationToken = default)
    {
        _persistCts?.Cancel();
        await PersistNowAsync(cancellationToken).ConfigureAwait(false);
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
        lock (_admissionGate)
        {
            _shuttingDown = true;
        }

        _suppressPersist = true;

        _persistCts?.Cancel();
        _persistCts?.Dispose();
        _persistCts = null;

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

            foreach (var document in Documents.ToArray())
            {
                if (!participants.Contains(document))
                {
                    participants.Add(document);
                    discovered = true;
                }
            }

            var round = new List<Task>(participants.Count + 1) { _work.WhenSettled() };

            foreach (var document in participants)
            {
                // Cancels synchronously and hands back the wait, so every tab is cancelled before the
                // first await below.
                round.Add(document.CancelAndDrainAsync());
            }

            // A round that discovered a tab never terminates the loop, even if everything looks quiet:
            // the new tab's constructor work may not have reached its work set yet.
            if (!discovered && round.TrueForAll(task => task.IsCompleted))
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
        if (_session is null || _suppressPersist)
        {
            return;
        }

        _persistCts?.Cancel();
        var cts = new CancellationTokenSource();
        _persistCts = cts;
        _work.Track(DelayedPersistAsync(cts.Token));
    }

    private async Task DelayedPersistAsync(CancellationToken token)
    {
        try
        {
            await Task.Delay(_sessionDebounce, token).ConfigureAwait(false);
            await PersistNowAsync(token).ConfigureAwait(false);
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
}
