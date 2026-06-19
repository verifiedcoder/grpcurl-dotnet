using System.Collections.ObjectModel;
using System.Runtime.CompilerServices;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GrpCurl.Net.Studio.ViewModels.Connections;
using GrpCurl.Net.Studio.ViewModels.Models;
using GrpCurl.Net.Studio.ViewModels.Models.Connections;
using GrpCurl.Net.Studio.ViewModels.Models.Descriptors;
using GrpCurl.Net.Studio.ViewModels.Models.Invocation;
using GrpCurl.Net.Studio.ViewModels.Panes;
using GrpCurl.Net.Studio.ViewModels.Services;
using GrpCurl.Net.Utilities;

namespace GrpCurl.Net.Studio.ViewModels.Documents;

/// <summary>
///     A unary invocation tab (FR-060..079): binds one connection + method, edits a JSON request,
///     runs it through <see cref="IInvocationRunner" /> (one CTS per tab via the cancel command,
///     ADR-014), and renders the response body + headers/trailers/status/timing. Off-thread work is
///     marshalled back through <see cref="IUiDispatcher" />. On failure it surfaces the rich
///     <see cref="ErrorModel" /> (FR-090..099) with Retry / Copy-as-JSON / Open-help-link.
/// </summary>
public sealed partial class InvocationDocumentViewModel : DocumentViewModel
{
    private readonly IInvocationRunner _runner;
    private readonly IDescriptorService _descriptors;
    private readonly IUiDispatcher _dispatcher;
    private readonly IClipboardService _clipboard;
    private readonly IDialogService _dialogs;
    private readonly ILauncherService _launcher;
    private readonly IRequestValidator _validator;
    private readonly IFilePickerService? _filePicker;
    private readonly IRevealGate _revealGate;
    private readonly IDocumentHost? _documentHost;
    private readonly ConsoleViewModel? _console;
    private readonly IInspector? _inspector;
    private readonly IHistoryRecorder? _recorder;
    private readonly ISavedRequestStore? _savedRequests;
    private readonly IEnvironmentService? _environment;
    private readonly TlsProfile? _tlsProfile;
    private InvocationStatusModel? _streamTerminalStatus;

    // FR-145/FR-002: the saved request this tab is bound to (null = unsaved draft) and the baseline
    // signature it was last saved/opened at, so divergence drives the dirty marker.
    private string? _savedRequestId;
    private string _savedRequestName = string.Empty;
    private string? _savedSignature;

    private CancellationTokenSource? _elapsedCts;
    private DateTimeOffset _elapsedStart;
    private DateTimeOffset? _deadlineAt;
    private readonly StreamDispatchPump _pump = new();
    private readonly Func<string, TextWriter> _writerFactory;
    private StreamCaptureWriter? _capture;
    private CancellationTokenSource? _validationCts;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsInFlight), nameof(IsCompleted), nameof(HasResponse))]
    [NotifyCanExecuteChangedFor(nameof(InvokeCommand), nameof(StartStreamCommand))]
    private RunState _state = RunState.Idle;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsStreaming), nameof(HasComposer), nameof(ShowMergedToggle))]
    [NotifyCanExecuteChangedFor(nameof(InvokeCommand), nameof(StartStreamCommand))]
    private StreamingShape _shape = StreamingShape.Unary;

    [ObservableProperty]
    private StreamComposerViewModel? _composer;

    /// <summary>Request/response wire sizes for the Timing tab (FR-110).</summary>
    [ObservableProperty]
    private string? _timingBytesText;

    /// <summary>FR-062: the request-body grammar/parser — JSON (default) or protobuf text format.</summary>
    [ObservableProperty]
    private RequestBodyFormat _bodyFormat = RequestBodyFormat.Json;

    /// <summary>FR-073: live elapsed time while a call is in flight.</summary>
    [ObservableProperty]
    private string? _elapsedText;

    /// <summary>FR-073: live deadline countdown when a deadline is set.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasDeadlineRemaining))]
    private string? _deadlineRemainingText;

    /// <summary>The verbose call transcript for the Raw tab (FR-111); header values are redacted (FR-112).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasRawTranscript))]
    [NotifyCanExecuteChangedFor(nameof(CopyRawTranscriptCommand))]
    private string? _rawTranscript;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError), nameof(HasErrorSuggestions), nameof(HasErrorDetails))]
    [NotifyCanExecuteChangedFor(nameof(RetryCommand), nameof(CopyErrorJsonCommand))]
    private ErrorModel? _error;

    [ObservableProperty]
    private StatusSeverity _severity = StatusSeverity.Ok;

    [ObservableProperty]
    private bool _isCapturing;

    [ObservableProperty]
    private string? _capturePath;

    [ObservableProperty]
    private long _captureBytes;

    [ObservableProperty]
    private string _requestJson = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasResponse))]
    [NotifyCanExecuteChangedFor(nameof(CopyResponseCommand))]
    private string? _responseJson;

    [ObservableProperty]
    private string _deadline = string.Empty;

    [ObservableProperty]
    private bool _emitDefaults;

    [ObservableProperty]
    private bool _allowUnknownFields = true;

    [ObservableProperty]
    private string _maxMessageSize = string.Empty;

    [ObservableProperty]
    private string? _statusText;

    [ObservableProperty]
    private bool _statusIsError;

    public InvocationDocumentViewModel(
        SavedConnection connection,
        string methodSymbol,
        string? initialRequestJson,
        IInvocationRunner runner,
        IDescriptorService descriptors,
        IUiDispatcher dispatcher,
        IClipboardService clipboard,
        IDialogService dialogs,
        ILauncherService launcher,
        IRequestValidator validator,
        IFilePickerService? filePicker = null,
        int ringCapacity = 10_000,
        Func<string, TextWriter>? writerFactory = null,
        IRevealGate? revealGate = null,
        IDocumentHost? documentHost = null,
        ConsoleViewModel? console = null,
        IInspector? inspector = null,
        IHistoryRecorder? recorder = null,
        ISavedRequestStore? savedRequests = null,
        IEnvironmentService? environment = null,
        TlsProfile? tlsProfile = null)
    {
        Connection = connection;
        MethodSymbol = methodSymbol;
        _runner = runner;
        _descriptors = descriptors;
        _dispatcher = dispatcher;
        _clipboard = clipboard;
        _dialogs = dialogs;
        _launcher = launcher;
        _validator = validator;
        _documentHost = documentHost;
        _filePicker = filePicker;
        _console = console;
        _inspector = inspector;
        _recorder = recorder;
        _savedRequests = savedRequests;
        _environment = environment;
        _tlsProfile = tlsProfile;
        _revealGate = revealGate ?? AlwaysRevealGate.Instance;
        _writerFactory = writerFactory ?? (path => new StreamWriter(path));

        Title = ShortName(methodSymbol);

        // FR-002: editing any persistable field re-evaluates divergence from the saved copy.
        PropertyChanged += OnTrackedPropertyChanged;

        // FR-133: switching the active environment refreshes every header's resolved-value preview.
        if (_environment is not null)
        {
            _environment.ActiveChanged += OnActiveEnvironmentChanged;
        }

        // FR-088: rows carry the clipboard + inspector so their context actions (copy JSON / NDJSON /
        // open in viewer) work without reaching back into this tab.
        var rowServices = new StreamRowServices(_clipboard, _runner.FormatMessageCompact, _inspector);
        Log = new StreamLogViewModel(ringCapacity, _runner.FormatMessage, rowServices);

        // FR-067: a header row's -bin validity gates Invoke, so re-evaluate when rows or values change.
        Headers.CollectionChanged += OnHeadersChanged;

        _ = ResolveMethodAsync(initialRequestJson);
    }

    private void OnHeadersChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        foreach (var row in e.OldItems?.OfType<HeaderRowViewModel>() ?? [])
        {
            row.PropertyChanged -= OnHeaderRowChanged;
        }

        foreach (var row in e.NewItems?.OfType<HeaderRowViewModel>() ?? [])
        {
            row.PropertyChanged += OnHeaderRowChanged;
            row.ActiveEnvironmentResolver = ResolveActiveEnvironmentVariable; // FR-066: env-aware preview
        }

        OnPropertyChanged(nameof(HasHeaderErrors));
        InvokeCommand.NotifyCanExecuteChanged();
        RefreshDirty(); // FR-002: adding/removing a header row diverges from the saved copy
    }

    /// <summary>FR-066/FR-131: the active environment's plain value for a variable (secrets stay redacted), or null.</summary>
    private string? ResolveActiveEnvironmentVariable(string name)
        => _environment?.Active?.Variables.FirstOrDefault(v => v.Name == name && !v.IsSecret)?.Value.Literal;

    private void OnActiveEnvironmentChanged(object? sender, EventArgs e)
    {
        foreach (var row in Headers)
        {
            row.RefreshResolvedPreview();
        }
    }

    private void OnHeaderRowChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(HeaderRowViewModel.HasBinError))
        {
            OnPropertyChanged(nameof(HasHeaderErrors));
            InvokeCommand.NotifyCanExecuteChanged();
        }

        if (e.PropertyName is nameof(HeaderRowViewModel.Name) or nameof(HeaderRowViewModel.Value))
        {
            RefreshDirty(); // FR-002: a header edit diverges from the saved copy
        }
    }

    public SavedConnection Connection { get; }

    public override SavedConnection? TabConnection => Connection;

    public string MethodSymbol { get; }

    /// <summary>FR-163: the shell dialect used by "Copy as CLI" (seeded from settings when the tab opens).</summary>
    public ShellDialect CliDialect { get; set; } = ShellDialect.Bash;

    public ObservableCollection<HeaderRowViewModel> Headers { get; } = [];
    public ObservableCollection<MetadataRowViewModel> ResponseHeaders { get; } = [];
    public ObservableCollection<MetadataRowViewModel> ResponseTrailers { get; } = [];
    public ObservableCollection<TimingRow> Timing { get; } = [];

    /// <summary>The streaming event log (FR-081); empty for unary tabs.</summary>
    public StreamLogViewModel Log { get; }

    public bool IsStreaming => Shape != StreamingShape.Unary;
    public bool HasComposer => Shape is StreamingShape.ClientStreaming or StreamingShape.BidiStreaming; // FR-082
    public bool ShowMergedToggle => Shape == StreamingShape.BidiStreaming;                              // FR-083

    /// <summary>FR-063 advisory request-validation problems (never block Invoke).</summary>
    public ObservableCollection<ValidationProblem> Problems { get; } = [];

    /// <summary>Debounce window before request validation runs after an edit; tests shorten it.</summary>
    internal TimeSpan ValidationDebounce { get; set; } = TimeSpan.FromMilliseconds(250);

    public bool IsInFlight => State == RunState.InFlight;
    public bool IsCompleted => State is RunState.Completed or RunState.Failed or RunState.Cancelled;
    public bool HasResponse => ResponseJson is not null;
    public bool HasError => Error is not null;
    public bool HasErrorSuggestions => Error is { Suggestions.Count: > 0 };
    public bool HasErrorDetails => Error is { Details.Count: > 0 };
    public bool HasProblems => Problems.Count > 0;

    /// <summary>FR-067: any header with an invalid <c>-bin</c> value blocks the call.</summary>
    public bool HasHeaderErrors => Headers.Any(h => h.HasBinError);

    private bool CanInvoke => !IsStreaming && State != RunState.InFlight && !HasHeaderErrors;
    private bool CanStartStream => IsStreaming && State != RunState.InFlight;

    // FR-063: re-validate (debounced, off-thread) whenever the body or the unknown-fields toggle changes.
    partial void OnRequestJsonChanged(string value) => ScheduleValidation();

    partial void OnAllowUnknownFieldsChanged(bool value) => ScheduleValidation();

    private void ScheduleValidation()
    {
        _validationCts?.Cancel();
        _validationCts?.Dispose();

        var cts = new CancellationTokenSource();
        _validationCts = cts;
        _ = DebouncedValidateAsync(cts.Token);
    }

    private async Task DebouncedValidateAsync(CancellationToken cancellationToken)
    {
        try
        {
            if (ValidationDebounce > TimeSpan.Zero)
            {
                await Task.Delay(ValidationDebounce, cancellationToken).ConfigureAwait(false);
            }

            await RunValidationAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Superseded by a newer edit; drop silently.
        }
    }

    internal async Task RunValidationAsync(CancellationToken cancellationToken = default)
    {
        // FR-062/063: the validator is JSON-aware; in protobuf-text mode it would only emit spurious
        // findings, so clear problems and let Core/the server stay the authority.
        if (BodyFormat == RequestBodyFormat.Text)
        {
            await _dispatcher.InvokeAsync(() =>
            {
                Problems.Clear();
                OnPropertyChanged(nameof(HasProblems));
            });
            return;
        }

        var problems = await _validator
            .ValidateAsync(Connection, MethodSymbol, RequestJson, AllowUnknownFields, cancellationToken)
            .ConfigureAwait(false);

        if (cancellationToken.IsCancellationRequested)
        {
            return;
        }

        await _dispatcher.InvokeAsync(() =>
        {
            Problems.Clear();

            foreach (var problem in problems)
            {
                Problems.Add(problem);
            }

            OnPropertyChanged(nameof(HasProblems));
        });
    }

    /// <summary>The two request-body grammars for the editor toggle (FR-062).</summary>
    public IReadOnlyList<RequestBodyFormat> BodyFormats { get; } = [RequestBodyFormat.Json, RequestBodyFormat.Text];

    partial void OnBodyFormatChanged(RequestBodyFormat value)
    {
        // Re-run (or clear) validation for the new grammar.
        _ = RunValidationAsync();

        // FR-062: a non-empty body is reinterpreted in the new format on send; offer to clear it.
        if (!string.IsNullOrWhiteSpace(RequestJson))
        {
            _ = WarnBodyReinterpretAsync();
        }
    }

    private async Task WarnBodyReinterpretAsync()
    {
        var clear = await _dialogs.ConfirmAsync(
            "Reinterpret request body?",
            "The request body will be parsed in the new format on send. Clear it now? (Cancel keeps the current text.)");

        if (clear)
        {
            await _dispatcher.InvokeAsync(() => RequestJson = string.Empty);
        }
    }

    // ── FR-073: live elapsed + deadline countdown while in flight ─────────────

    public bool HasDeadlineRemaining => DeadlineRemainingText is not null;

    partial void OnStateChanged(RunState value)
    {
        if (value == RunState.InFlight)
        {
            BeginElapsed(DateTimeOffset.UtcNow, ComputeDeadlineAt());
            _elapsedCts?.Cancel();
            var cts = new CancellationTokenSource();
            _elapsedCts = cts;
            _ = TickElapsedAsync(cts.Token);
        }
        else
        {
            _elapsedCts?.Cancel();
            _elapsedCts = null;
        }
    }

    /// <summary>Seeds the elapsed/deadline clock (test seam; the timer drives it during a real call).</summary>
    internal void BeginElapsed(DateTimeOffset start, DateTimeOffset? deadlineAt)
    {
        _elapsedStart = start;
        _deadlineAt = deadlineAt;
        UpdateElapsed(start);
    }

    /// <summary>Recomputes the live elapsed + deadline-remaining text for the given instant.</summary>
    internal void UpdateElapsed(DateTimeOffset now)
    {
        ElapsedText = $"{(now - _elapsedStart).TotalSeconds:0.0}s elapsed";
        DeadlineRemainingText = _deadlineAt is { } deadline
            ? $"{Math.Max(0, (deadline - now).TotalSeconds):0.0}s to deadline"
            : null;
    }

    private DateTimeOffset? ComputeDeadlineAt()
    {
        if (string.IsNullOrWhiteSpace(Deadline))
        {
            return null;
        }

        try
        {
            return DateTimeOffset.UtcNow.Add(GrpcChannelFactory.ParseDuration(Deadline));
        }
        catch (ArgumentException)
        {
            return null; // invalid duration — no countdown (the call will surface the parse error)
        }
    }

    private async Task TickElapsedAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                await Task.Delay(250, cancellationToken).ConfigureAwait(false);
                await _dispatcher.InvokeAsync(() => UpdateElapsed(DateTimeOffset.UtcNow)).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // Stopped when the call completed or was cancelled.
        }
    }

    [RelayCommand(CanExecute = nameof(CanInvoke), IncludeCancelCommand = true)]
    private async Task Invoke(CancellationToken cancellationToken)
    {
        await _dispatcher.InvokeAsync(() =>
        {
            State = RunState.InFlight;
            ResponseJson = null;
            StatusText = null;
            StatusIsError = false;
            Error = null;
            Severity = StatusSeverity.Ok;
            ResponseHeaders.Clear();
            ResponseTrailers.Clear();
            Timing.Clear();
            TimingBytesText = null;
            RawTranscript = null;
        });

        var request = BuildRequest();

        try
        {
            var result = await _runner.InvokeUnaryAsync(request, cancellationToken);
            await _dispatcher.InvokeAsync(() => Apply(result));
            await RecordHistoryAsync(r => r.RecordUnaryAsync(request, result, cancellationToken));
        }
        catch (OperationCanceledException)
        {
            await _dispatcher.InvokeAsync(() =>
            {
                State = RunState.Cancelled;
                StatusText = "Cancelled";
                StatusIsError = true;
                Severity = StatusSeverity.Cancelled;
            });
        }
    }

    /// <summary>FR-120: records the call to history, best-effort — a history failure never breaks the invoke.</summary>
    private async Task RecordHistoryAsync(Func<IHistoryRecorder, Task> record)
    {
        if (_recorder is null)
        {
            return;
        }

        try
        {
            await record(_recorder);
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException)
        {
            // History is a convenience; never surface a persistence hiccup to the invoke flow.
        }
    }

    [RelayCommand(CanExecute = nameof(CanStartStream), IncludeCancelCommand = true)]
    private async Task StartStream(CancellationToken cancellationToken)
    {
        await _dispatcher.InvokeAsync(() =>
        {
            State = RunState.InFlight;
            Error = null;
            StatusText = null;
            StatusIsError = false;
            Severity = StatusSeverity.Ok;
            Log.Reset();
        });

        _streamTerminalStatus = null;

        // Client/duplex stream request bodies from the composer; server-streaming sends the editor body once.
        var requestJson = HasComposer && Composer is not null ? Composer.Begin() : Once(RequestJson);

        try
        {
            await _pump.RunAsync(
                CaptureTap(_runner.InvokeStreamingAsync(BuildStreamRequest(), requestJson, cancellationToken), cancellationToken),
                batch => _dispatcher.InvokeAsync(() => ApplyStreamBatch(batch)),
                cancellationToken);

            await _dispatcher.InvokeAsync(() =>
            {
                if (State == RunState.InFlight)
                {
                    State = RunState.Completed;
                }
            });
        }
        catch (OperationCanceledException)
        {
            await _dispatcher.InvokeAsync(() =>
            {
                // FR-084: received rows are preserved; the final row records the cancellation.
                var detail = string.IsNullOrWhiteSpace(Deadline) ? "Cancelled" : "Deadline reached (client).";
                Log.Append(new StreamEventModel(
                    StreamEventKind.Status, -1, DateTimeOffset.Now, 0, detail,
                    Status: new InvocationStatusModel(1, "Cancelled", detail)));
                State = RunState.Cancelled;
                StatusText = "Cancelled";
                StatusIsError = true;
                Severity = StatusSeverity.Cancelled;
            });
        }
        finally
        {
            Composer?.End();
        }

        // FR-120: record the completed/cancelled stream — counts + terminal status, never the messages.
        var status = _streamTerminalStatus
                     ?? (State == RunState.Cancelled
                         ? new InvocationStatusModel(1, "Cancelled", string.Empty)
                         : new InvocationStatusModel(0, "OK", string.Empty));
        await RecordHistoryAsync(r => r.RecordStreamAsync(
            BuildStreamRequest(), status, Log.ElapsedMs, (int)Log.TotalSent, (int)Log.TotalReceived, cancellationToken));
    }

    private void ApplyStreamBatch(IReadOnlyList<StreamEventModel> batch)
    {
        Log.Append(batch);

        if (_capture is not null)
        {
            CaptureBytes = _capture.BytesWritten; // live capture-size readout (FR-086)
        }

        foreach (var ev in batch)
        {
            if (ev.Kind != StreamEventKind.Status)
            {
                continue;
            }

            Error = ev.Error;
            Severity = ev.Error?.Severity ?? (ev.Status is { } s ? StatusSeverityMap.FromCode(s.Code) : StatusSeverity.Ok);
            StatusIsError = ev.Status is { Code: not 0 };
            StatusText = ev.Status?.CodeName;

            if (ev.Status is { } terminal)
            {
                _streamTerminalStatus = terminal; // FR-120: capture the stream's final status for history
            }
        }
    }

    // FR-086: spill every event to NDJSON as it arrives (before the ring buffer), if capture is on.
    private async IAsyncEnumerable<StreamEventModel> CaptureTap(
        IAsyncEnumerable<StreamEventModel> source, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (var ev in source.WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            if (_capture is { } capture)
            {
                await capture.WriteAsync(ev).ConfigureAwait(false);
            }

            yield return ev;
        }
    }

    /// <summary>FR-086: toggle NDJSON capture-to-disk (available before and during a stream).</summary>
    [RelayCommand]
    private async Task ToggleCapture()
    {
        if (IsCapturing)
        {
            _capture?.Dispose();
            _capture = null;
            IsCapturing = false;
            return;
        }

        if (_filePicker is null)
        {
            return;
        }

        var path = await _filePicker.SaveFileAsync("Capture stream to disk", "capture.ndjson", [".ndjson"]);

        if (path is null)
        {
            return;
        }

        _capture = new StreamCaptureWriter(_writerFactory(path), _runner.FormatMessageCompact);
        CapturePath = path;
        CaptureBytes = 0;
        IsCapturing = true;
    }

    /// <summary>FR-087: export the retained event rows as an NDJSON file.</summary>
    [RelayCommand]
    private async Task ExportStream()
    {
        if (_filePicker is null)
        {
            return;
        }

        var path = await _filePicker.SaveFileAsync("Export stream", "stream.ndjson", [".ndjson", ".json"]);

        if (path is null)
        {
            return;
        }

        await using var writer = _writerFactory(path);

        foreach (var row in Log.Rows)
        {
            await writer.WriteLineAsync(NdjsonStreamFormatter.Format(row.Event, _runner.FormatMessageCompact));
        }
    }

    private static async IAsyncEnumerable<string> Once(string json)
    {
        yield return json;
        await Task.CompletedTask;
    }

    private StreamRequestModel BuildStreamRequest() => new(
        Connection,
        MethodSymbol,
        Headers.Select(h => h.ToEntry()).ToList(),
        NullIfBlank(Deadline),
        EmitDefaults,
        AllowUnknownFields,
        NullIfBlank(MaxMessageSize),
        BodyFormat);

    private void Apply(InvocationResultModel result)
    {
        ResponseJson = result.ResponseJson;

        foreach (var header in result.ResponseHeaders)
        {
            ResponseHeaders.Add(new MetadataRowViewModel(header, _revealGate));
        }

        foreach (var trailer in result.ResponseTrailers)
        {
            ResponseTrailers.Add(new MetadataRowViewModel(trailer, _revealGate));
        }

        AddTimingRows(result.Timing);
        RawTranscript = result.Transcript is { } transcript ? VerboseTranscriptFormatter.Format(transcript) : null;

        Error = result.Error;
        Severity = result.Error?.Severity ?? StatusSeverityMap.FromCode(result.Status.Code);
        StatusIsError = !result.Ok;
        StatusText = result.Status.CodeName; // FR-091: pill text is always the status name
        State = result.Ok ? RunState.Completed : RunState.Failed;

        RecordCallActivity(result.Status, result.Timing); // FR-114: log the completed call to the console
    }

    /// <summary>FR-114: append a completed call (status + phase breakdown) to the console activity log.</summary>
    private void RecordCallActivity(InvocationStatusModel status, TimingModel timing)
    {
        if (_console is null)
        {
            return;
        }

        var total = timing.Phases.FirstOrDefault(p => p.Phase == "total")?.Duration
                    ?? timing.Phases.Aggregate(TimeSpan.Zero, (acc, p) => acc + p.Duration);
        var totalMs = total.TotalMilliseconds;

        var phases = timing.Phases
            .Where(p => p.Phase != "total")
            .Select(p => new CallTimingPhase(
                p.Phase,
                $"{p.Duration.TotalMilliseconds:0} ms",
                totalMs > 0 ? p.Duration.TotalMilliseconds / totalMs : 0.0))
            .ToList();

        _console.AppendCall(new ConsoleCallActivity(
            MethodSymbol, status.Code, status.CodeName, status.Code != 0, $"{totalMs:0} ms", phases,
            ConsoleActivityKind.Invocation, DateTimeOffset.UtcNow));
    }

    /// <summary>Builds the Timing-tab rows with each phase's fraction of the total for the bar breakdown (FR-110).</summary>
    private void AddTimingRows(TimingModel timing)
    {
        var total = timing.Phases.FirstOrDefault(p => p.Phase == "total")?.Duration
                    ?? timing.Phases.Aggregate(TimeSpan.Zero, (acc, p) => acc + p.Duration);
        var totalMs = total.TotalMilliseconds;

        foreach (var phase in timing.Phases)
        {
            var isTotal = phase.Phase == "total";
            var fraction = isTotal ? 1.0 : totalMs > 0 ? phase.Duration.TotalMilliseconds / totalMs : 0.0;
            Timing.Add(new TimingRow(phase.Phase, phase.Duration, fraction, isTotal));
        }

        TimingBytesText = $"request {timing.RequestBytes} B · response {timing.ResponseBytes} B";
    }

    [RelayCommand(CanExecute = nameof(HasResponse))]
    private async Task CopyResponse()
    {
        if (ResponseJson is not null)
        {
            await _clipboard.SetTextAsync(ResponseJson);
        }
    }

    /// <summary>FR-074: save the response body to a file.</summary>
    [RelayCommand(CanExecute = nameof(HasResponse))]
    private async Task SaveResponse()
    {
        if (_filePicker is null || ResponseJson is null)
        {
            return;
        }

        var path = await _filePicker.SaveFileAsync("Save response", "response.json", [".json", ".txt"]);

        if (path is not null)
        {
            await using var writer = _writerFactory(path);
            await writer.WriteAsync(ResponseJson);
        }
    }

    /// <summary>Copies the equivalent grpcn invoke command, secrets as ${VAR} placeholders (FR-160/161).</summary>
    [RelayCommand]
    private async Task CopyAsCli()
    {
        // FR-165: a client/duplex tab reproduces its interactively-composed messages; everything else is unary.
        var command = HasComposer && Composer is { } composer
            ? CliCommandBuilder.BuildStreamingCommand(BuildRequest(), StreamingCliMessages(composer), CliDialect, _tlsProfile)
            : CliCommandBuilder.BuildCommand(BuildRequest(), CliDialect, _tlsProfile);

        await _clipboard.SetTextAsync(command);
    }

    /// <summary>The composed messages to reproduce in a streaming copy-as-CLI: those sent, or the current draft.</summary>
    private static IReadOnlyList<string> StreamingCliMessages(StreamComposerViewModel composer)
        => composer.SentMessages.Count > 0 ? composer.SentMessages : [composer.MessageJson];

    /// <summary>FR-095: a suggestion that maps to a Studio setting opens the Settings tab.</summary>
    [RelayCommand]
    private void OpenSettingLink(string? settingLink)
    {
        if (!string.IsNullOrEmpty(settingLink))
        {
            _documentHost?.OpenSettings();
        }
    }

    public bool HasRawTranscript => !string.IsNullOrEmpty(RawTranscript);

    /// <summary>Copies the verbose transcript (FR-111); redaction already applied — no secret literals.</summary>
    [RelayCommand(CanExecute = nameof(HasRawTranscript))]
    private async Task CopyRawTranscript()
    {
        if (RawTranscript is not null)
        {
            await _clipboard.SetTextAsync(RawTranscript);
        }
    }

    /// <summary>FR-093: re-run the call that failed.</summary>
    [RelayCommand(CanExecute = nameof(HasError))]
    private async Task Retry() => await InvokeCommand.ExecuteAsync(null);

    /// <summary>FR-099: copy the error as the CLI-parity JSON envelope.</summary>
    [RelayCommand(CanExecute = nameof(HasError))]
    private async Task CopyErrorJson()
    {
        if (Error is not null)
        {
            await _clipboard.SetTextAsync(Error.JsonEnvelope);
        }
    }

    /// <summary>FR-094: open a google.rpc.Help link after confirming with the user.</summary>
    [RelayCommand]
    private async Task OpenHelpLink(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return;
        }

        if (await _dialogs.ConfirmAsync("Open link", $"Open {url} in your browser?"))
        {
            await _launcher.LaunchUriAsync(url);
        }
    }

    private InvocationRequestModel BuildRequest() => new(
        Connection,
        MethodSymbol,
        RequestJson,
        Headers.Select(h => h.ToEntry()).ToList(),
        NullIfBlank(Deadline),
        EmitDefaults,
        AllowUnknownFields,
        NullIfBlank(MaxMessageSize),
        BodyFormat);

    // ── FR-145 / FR-078 / FR-002: save the tab as a named request + track divergence ──

    /// <summary>The header text: the request name with a dirty dot when it diverges from the saved copy.</summary>
    public override string DisplayTitle => IsSavedRequestDirty ? $"{Title} ●" : Title;

    /// <summary>FR-002: true when this tab is bound to a saved request and has unsaved edits.</summary>
    public bool IsSavedRequestDirty => _savedRequestId is not null && _savedSignature != CurrentSignature();

    /// <summary>Whether the Save action is available (the saved-request store is wired).</summary>
    public bool CanSaveRequest => _savedRequests is not null;

    /// <summary>
    ///     Binds this tab to a saved request and snapshots the baseline at <paramref name="expectedBody" />
    ///     (the body the async method-resolve will settle <see cref="RequestJson" /> to), so the tab opens clean.
    /// </summary>
    public void BindSavedRequest(string id, string name, string expectedBody)
    {
        _savedRequestId = id;
        _savedRequestName = name;
        _savedSignature = SignatureWith(expectedBody);
        RefreshDirty();
    }

    /// <summary>
    ///     FR-078: promote the tab to a named <see cref="SavedRequest" /> (prompting for a name the first time),
    ///     or update the request it is already bound to. On success the title takes the name and the dirty
    ///     marker clears.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanSaveRequest))]
    private async Task SaveRequest()
    {
        if (_savedRequests is null)
        {
            return;
        }

        var name = _savedRequestName;

        if (_savedRequestId is null)
        {
            var entered = await _dialogs.ShowDialogAsync(
                new TextInputDialogViewModel("Save request", "Name", ShortName(MethodSymbol)));

            if (string.IsNullOrWhiteSpace(entered))
            {
                return;
            }

            name = entered;
            _savedRequestId = Guid.NewGuid().ToString();
        }

        await _savedRequests.SaveAsync(ToSavedRequest(_savedRequestId, name));

        _savedRequestName = name;
        Title = name; // FR-078: the tab title takes the request name
        _savedSignature = CurrentSignature();
        RefreshDirty();
    }

    private SavedRequest ToSavedRequest(string id, string name) => new()
    {
        Id = id,
        Name = name,
        ConnectionId = Connection.Id,
        Method = MethodSymbol,
        BodyFormat = BodyFormat,
        Body = RequestJson,
        Headers = Headers.Select(h => h.ToEntry()).ToList(),
        Deadline = NullIfBlank(Deadline),
        EmitDefaults = EmitDefaults,
        AllowUnknownFields = AllowUnknownFields,
        MaxReceiveBytes = ParseBytes(MaxMessageSize)
    };

    private string CurrentSignature() => SignatureWith(RequestJson);

    /// <summary>A stable signature of the persistable request state, used to detect divergence (FR-002).</summary>
    private string SignatureWith(string body)
    {
        var headers = string.Join("", Headers.Select(h => $"{h.Name}{h.Value}{h.IsBin}"));
        return string.Join(
            "",
            MethodSymbol,
            body,
            BodyFormat.ToString(),
            NullIfBlank(Deadline) ?? string.Empty,
            EmitDefaults ? "1" : "0",
            AllowUnknownFields ? "1" : "0",
            NullIfBlank(MaxMessageSize) ?? string.Empty,
            headers);
    }

    private void OnTrackedPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(RequestJson) or nameof(BodyFormat) or nameof(Deadline)
            or nameof(EmitDefaults) or nameof(AllowUnknownFields) or nameof(MaxMessageSize))
        {
            RefreshDirty();
        }
    }

    private void RefreshDirty()
    {
        OnPropertyChanged(nameof(IsSavedRequestDirty));
        OnPropertyChanged(nameof(DisplayTitle));
    }

    private static long? ParseBytes(string? value)
        => long.TryParse(value, out var parsed) && parsed > 0 ? parsed : null;

    [RelayCommand]
    private void AddHeader() => Headers.Add(new HeaderRowViewModel());

    [RelayCommand]
    private void RemoveHeader(HeaderRowViewModel? row)
    {
        if (row is not null)
        {
            Headers.Remove(row);
        }
    }

    private async Task ResolveMethodAsync(string? initialRequestJson)
    {
        if (initialRequestJson is not null)
        {
            RequestJson = initialRequestJson;
        }

        var result = await _descriptors.DescribeAsync(Connection, MethodSymbol, CancellationToken.None);

        await _dispatcher.InvokeAsync(() =>
        {
            if (result is not { Ok: true, Symbol: MethodDescription method })
            {
                return;
            }

            Shape = method.Shape;

            if (initialRequestJson is null)
            {
                RequestJson = method.TemplateJson;
            }

            if (HasComposer)
            {
                Composer = new StreamComposerViewModel(
                    Connection, MethodSymbol, AllowUnknownFields, _validator, _dispatcher, _filePicker);
            }
        });
    }

    private static string? NullIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;

    private static string ShortName(string symbol)
    {
        var trimmed = symbol.TrimEnd('.', '/');
        var last = trimmed.LastIndexOfAny(['.', '/']);
        return last >= 0 && last < trimmed.Length - 1 ? trimmed[(last + 1)..] : trimmed;
    }
}
