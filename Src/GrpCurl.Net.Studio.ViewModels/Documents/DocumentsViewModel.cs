using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using GrpCurl.Net.Studio.ViewModels.Connections;
using GrpCurl.Net.Studio.ViewModels.Models.Connections;
using GrpCurl.Net.Studio.ViewModels.Models.Invocation;
using GrpCurl.Net.Studio.ViewModels.Panes;
using GrpCurl.Net.Studio.ViewModels.Services;

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

    [ObservableProperty]
    private DocumentViewModel? _selectedDocument;

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
        IDiagnosticsLog? diagnostics = null)
    {
        _descriptors = descriptors;
        _dispatcher = dispatcher;
        _clipboard = clipboard;
        _invocation = invocation;
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
        var document = new InvocationDocumentViewModel(
            connection, method, body, _invocation, _descriptors, _dispatcher, _clipboard, _dialogs, _launcher, _validator,
            _filePicker, _settings.Current.Network.RingBufferSize, revealGate: _revealGate, documentHost: this,
            console: _console, inspector: _inspector, recorder: _recorder, savedRequests: _savedRequests,
            environment: _environment);

        document.CliDialect = _settings.Current.General.CliShellDialect;
        return document;
    }

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

        var document = new HistoryDocumentViewModel(_history, _settings, _workspace, this, _dialogs, _dispatcher, _filePicker);
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
        Documents.Remove(document);

        if (SelectedDocument == document)
        {
            SelectedDocument = Documents.Count == 0
                ? null
                : Documents[Math.Min(index, Documents.Count - 1)];
        }
    }
}
