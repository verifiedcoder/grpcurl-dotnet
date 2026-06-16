using System.Collections.ObjectModel;
using System.Runtime.CompilerServices;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GrpCurl.Net.Studio.ViewModels.Connections;
using GrpCurl.Net.Studio.ViewModels.Models;
using GrpCurl.Net.Studio.ViewModels.Models.Connections;
using GrpCurl.Net.Studio.ViewModels.Models.Descriptors;
using GrpCurl.Net.Studio.ViewModels.Models.Invocation;
using GrpCurl.Net.Studio.ViewModels.Services;

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
        Func<string, TextWriter>? writerFactory = null)
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
        _filePicker = filePicker;
        _writerFactory = writerFactory ?? (path => new StreamWriter(path));

        Title = ShortName(methodSymbol);
        Log = new StreamLogViewModel(ringCapacity, _runner.FormatMessage);

        _ = ResolveMethodAsync(initialRequestJson);
    }

    public SavedConnection Connection { get; }

    public override SavedConnection? TabConnection => Connection;

    public string MethodSymbol { get; }

    /// <summary>FR-163: the shell dialect used by "Copy as CLI" (seeded from settings when the tab opens).</summary>
    public ShellDialect CliDialect { get; set; } = ShellDialect.Bash;

    public ObservableCollection<HeaderRowViewModel> Headers { get; } = [];
    public ObservableCollection<MetadataItem> ResponseHeaders { get; } = [];
    public ObservableCollection<MetadataItem> ResponseTrailers { get; } = [];
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

    private bool CanInvoke => !IsStreaming && State != RunState.InFlight;
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
        });

        try
        {
            var result = await _runner.InvokeUnaryAsync(BuildRequest(), cancellationToken);
            await _dispatcher.InvokeAsync(() => Apply(result));
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
        NullIfBlank(MaxMessageSize));

    private void Apply(InvocationResultModel result)
    {
        ResponseJson = result.ResponseJson;

        foreach (var header in result.ResponseHeaders)
        {
            ResponseHeaders.Add(header);
        }

        foreach (var trailer in result.ResponseTrailers)
        {
            ResponseTrailers.Add(trailer);
        }

        AddTimingRows(result.Timing);

        Error = result.Error;
        Severity = result.Error?.Severity ?? StatusSeverityMap.FromCode(result.Status.Code);
        StatusIsError = !result.Ok;
        StatusText = result.Status.CodeName; // FR-091: pill text is always the status name
        State = result.Ok ? RunState.Completed : RunState.Failed;
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

    /// <summary>Copies the equivalent grpcn invoke command, secrets as ${VAR} placeholders (FR-160/161).</summary>
    [RelayCommand]
    private async Task CopyAsCli()
        => await _clipboard.SetTextAsync(CliCommandBuilder.BuildCommand(BuildRequest(), CliDialect));

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
        NullIfBlank(MaxMessageSize));

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
