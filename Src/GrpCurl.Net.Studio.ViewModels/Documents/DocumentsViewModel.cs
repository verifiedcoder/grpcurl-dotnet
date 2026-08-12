using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GrpCurl.Net.Studio.ViewModels.Connections;
using GrpCurl.Net.Studio.ViewModels.Models;
using GrpCurl.Net.Studio.ViewModels.Models.Connections;
using GrpCurl.Net.Studio.ViewModels.Models.Invocation;
using GrpCurl.Net.Studio.ViewModels.Models.Session;
using GrpCurl.Net.Studio.ViewModels.Panes;
using GrpCurl.Net.Studio.ViewModels.Services;
using System.Collections.ObjectModel;
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
        document.CloseRequested += OnDocumentCloseRequested;

        Documents.Add(document);
        SelectedDocument = document;
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
        document.CloseRequested += OnDocumentCloseRequested;
        Documents.Add(document);
        SelectedDocument = document;
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
        document.CloseRequested += OnDocumentCloseRequested;

        Documents.Add(document);
        SelectedDocument = document;
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
        document.CloseRequested += OnDocumentCloseRequested;

        Documents.Add(document);
        SelectedDocument = document;
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
            (document as IDisposable)?.Dispose();
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
        (document as IDisposable)?.Dispose();
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
        // Belt and braces against the same overwrite: nothing should schedule a persist from here.
        _suppressPersist = true;

        _persistCts?.Cancel();
        _persistCts?.Dispose();
        _persistCts = null;

        // Snapshot: disposal must not observe the collection changing, and the two phases below have to
        // walk exactly the same set of tabs.
        var documents = Documents.ToArray();

        var drains = new List<Task>(documents.Length);

        foreach (var document in documents)
        {
            if (document is IDrainableDocument drainable)
            {
                // Cancels synchronously and hands back the wait — see IDrainableDocument. The loop
                // therefore cancels every tab before the first await below.
                drains.Add(drainable.CancelAndDrainAsync());
            }
        }

        var drained = true;

        if (drains.Count > 0)
        {
            try
            {
                await Task.WhenAll(drains).WaitAsync(drainTimeout).ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
                drained = false;
            }
        }

        foreach (var document in documents)
        {
            (document as IDisposable)?.Dispose();
        }

        return new DocumentShutdownResult(documents.Length, drained);
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
        _ = DelayedPersistAsync(cts.Token);
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
