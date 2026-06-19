using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GrpCurl.Net.Studio.ViewModels.Connections;
using GrpCurl.Net.Studio.ViewModels.Models;
using GrpCurl.Net.Studio.ViewModels.Models.Connections;
using GrpCurl.Net.Studio.ViewModels.Models.GraphQl;
using GrpCurl.Net.Studio.ViewModels.Services;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace GrpCurl.Net.Studio.ViewModels.Documents;

/// <summary>
///     A GraphQL operation tab (SPEC-015 E4.1): edits a GraphQL document + variables against a
///     connection, picks the operation to run, and executes it through <see cref="IGraphQlService" />
///     (one CTS per tab via the cancel command, ADR-014). The document is parsed live (debounced) to
///     populate the operation picker and surface syntax problems that block Execute (GQL-011/012).
///     This PR covers query/mutation execution + the response envelope viewer; subscriptions, the
///     mapping designer, and the introspection viewer arrive in later epics.
/// </summary>
public sealed partial class GraphQlDocumentViewModel : DocumentViewModel
{
    /// <summary>The 4 MiB cap the CLI applies to <c>--file</c>/<c>--variables-file</c> (GQL-014/020).</summary>
    internal const long MaxFileBytes = 4L * 1024 * 1024;

    private readonly IGraphQlService _graphql;
    private readonly IUiDispatcher _dispatcher;
    private readonly IClipboardService _clipboard;
    private readonly IHistoryRecorder? _recorder;
    private readonly IEnvironmentService? _environment;
    private readonly IFilePickerService? _filePicker;
    private readonly TlsProfile? _tlsProfile;
    private readonly Func<string, TextWriter> _writerFactory;
    private readonly Func<string, long, CancellationToken, Task<string>> _fileReader;
    private CancellationTokenSource? _parseCts;
    private bool _syncingVars;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsInFlight), nameof(IsCompleted), nameof(HasResponse), nameof(IsCancelled))]
    [NotifyCanExecuteChangedFor(nameof(ExecuteCommand))]
    public partial RunState State { get; set; } = RunState.Idle;

    [ObservableProperty]
    public partial string Document { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string VariablesJson { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string DefaultService { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ExecuteCommand))]
    [NotifyPropertyChangedFor(nameof(IsSubscription))]
    public partial GraphQlOperationInfo? SelectedOperation { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasResponse))]
    [NotifyCanExecuteChangedFor(nameof(CopyResponseCommand))]
    public partial string? ResponseJson { get; set; }

    [ObservableProperty]
    public partial bool EmitDefaults { get; set; }

    [ObservableProperty]
    public partial bool AllowUnknownFields { get; set; } = true;

    [ObservableProperty]
    public partial bool StrictSelection { get; set; }

    [ObservableProperty]
    public partial bool Introspection { get; set; } = true;

    /// <summary>GQL-023: emit the unprojected gRPC JSON (bypass selection projection); parity with CLI <c>--raw</c>.</summary>
    [ObservableProperty]
    public partial bool Raw { get; set; }

    /// <summary>GQL-029: verbose-pane level — off, resolved mapping (-v), or +request JSON (-vv).</summary>
    [ObservableProperty]
    public partial GraphQlVerbosity Verbosity { get; set; } = GraphQlVerbosity.Off;

    [ObservableProperty]
    public partial string Deadline { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string? StatusText { get; set; }

    [ObservableProperty]
    public partial bool StatusIsError { get; set; }

    public GraphQlDocumentViewModel(
        SavedConnection connection,
        IGraphQlService graphql,
        IUiDispatcher dispatcher,
        IClipboardService clipboard,
        IHistoryRecorder? recorder = null,
        IEnvironmentService? environment = null,
        IFilePickerService? filePicker = null,
        TlsProfile? tlsProfile = null,
        Func<string, TextWriter>? writerFactory = null,
        Func<string, long, CancellationToken, Task<string>>? fileReader = null)
    {
        Connection = connection;
        _graphql = graphql;
        _dispatcher = dispatcher;
        _clipboard = clipboard;
        _recorder = recorder;
        _environment = environment;
        _filePicker = filePicker;
        _tlsProfile = tlsProfile;
        _writerFactory = writerFactory ?? (path => new StreamWriter(path));
        _fileReader = fileReader ?? ReadCappedAsync;
        Title = "GraphQL";

        Headers.CollectionChanged += OnHeadersChanged;
    }

    /// <summary>Default file read: rejects a file larger than <see cref="MaxFileBytes" /> (CLI parity), else reads it.</summary>
    private static async Task<string> ReadCappedAsync(string path, long maxBytes, CancellationToken cancellationToken)
    {
        var info = new FileInfo(path);

        if (info.Exists && info.Length > maxBytes)
        {
            throw new InvalidOperationException($"File exceeds the {maxBytes / (1024 * 1024)} MiB limit.");
        }

        return await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
    }

    public SavedConnection Connection { get; }

    public override SavedConnection? TabConnection => Connection;

    /// <summary>FR-163: the shell dialect used by "Copy as CLI" (seeded from settings when the tab opens).</summary>
    public ShellDialect CliDialect { get; set; } = ShellDialect.Bash;

    /// <summary>The operations found in the current document (drives the picker, GQL-012).</summary>
    public ObservableCollection<GraphQlOperationInfo> Operations { get; } = [];

    /// <summary>Request metadata headers (GQL-003) — shared editor with invocation tabs.</summary>
    public ObservableCollection<HeaderRowViewModel> Headers { get; } = [];

    /// <summary>Syntax problems (GQL-011) and pre-RPC configuration/variable errors (AC-5); blocks Execute when any are syntax.</summary>
    public ObservableCollection<GraphQlProblem> Problems { get; } = [];

    /// <summary>Per-root-field execution progress rows (GQL-024 / AC-6); populated live during Execute.</summary>
    public ObservableCollection<GraphQlFieldProgressRow> FieldProgress { get; } = [];

    /// <summary>Quick-vars grid rows for the selected operation's declared variables (GQL-018).</summary>
    public ObservableCollection<GraphQlVariableRow> VariableRows { get; } = [];

    /// <summary>Unbound-required / undeclared-bound variable warnings (GQL-019).</summary>
    public ObservableCollection<GraphQlProblem> VariableWarnings { get; } = [];

    public bool HasVariableRows => VariableRows.Count > 0;
    public bool HasVariableWarnings => VariableWarnings.Count > 0;

    /// <summary>The subscription streaming console (GQL-060..065); used when the selected operation is a subscription.</summary>
    public GraphQlStreamLogViewModel StreamLog { get; } = new();

    /// <summary>True when the selected operation is a subscription (Execute streams into <see cref="StreamLog" />).</summary>
    public bool IsSubscription => SelectedOperation?.Kind == GraphQlOperationKind.Subscription;

    /// <summary>Captured verbose-pane lines from the last execution (GQL-029).</summary>
    public ObservableCollection<string> VerboseLog { get; } = [];

    public bool HasVerboseLog => VerboseLog.Count > 0;

    /// <summary>Structured <c>errors[]</c> from the last response (GQL-070), config-vs-upstream distinct (GQL-073).</summary>
    public ObservableCollection<GraphQlErrorInfo> Errors { get; } = [];

    public bool HasErrors => Errors.Count > 0;

    /// <summary>The verbosity options for the pane selector.</summary>
    public IReadOnlyList<GraphQlVerbosity> Verbosities { get; } =
        [GraphQlVerbosity.Off, GraphQlVerbosity.Verbose, GraphQlVerbosity.VeryVerbose];

    /// <summary>Debounce before the document is re-parsed after an edit; tests shorten it.</summary>
    internal TimeSpan ParseDebounce { get; set; } = TimeSpan.FromMilliseconds(300);

    public bool IsInFlight => State == RunState.InFlight;
    public bool IsCompleted => State is RunState.Completed or RunState.Failed or RunState.Cancelled;
    public bool IsCancelled => State == RunState.Cancelled;
    public bool HasResponse => ResponseJson is not null;
    public bool HasProblems => Problems.Count > 0;
    public bool HasFieldProgress => FieldProgress.Count > 0;

    /// <summary>GQL-067 parity: any header with an invalid <c>-bin</c> value blocks execution.</summary>
    public bool HasHeaderErrors => Headers.Any(h => h.HasBinError);

    /// <summary>A syntax problem makes the document unexecutable (GQL-011); a chosen operation is required (GQL-012).</summary>
    public bool HasSyntaxError => Problems.Any(p => p.Kind == GraphQlProblemKind.Syntax);

    private bool CanExecute => State != RunState.InFlight && SelectedOperation is not null && !HasSyntaxError && !HasHeaderErrors;

    partial void OnDocumentChanged(string value) => ScheduleParse();

    // GQL-018: the selected operation determines the quick-vars rows; rebuild them when it changes.
    partial void OnSelectedOperationChanged(GraphQlOperationInfo? value) => RebuildVariableRows();

    // GQL-018: a JSON edit (typing or import) re-pulls grid values + recomputes warnings, unless the grid drove it.
    partial void OnVariablesJsonChanged(string value)
    {
        if (!_syncingVars)
        {
            PullValuesFromJson();
        }
    }

    private void RebuildVariableRows()
    {
        foreach (var row in VariableRows)
        {
            row.PropertyChanged -= OnVariableRowChanged;
        }

        VariableRows.Clear();

        foreach (var variable in SelectedOperation?.Variables ?? [])
        {
            var row = new GraphQlVariableRow(variable.Name, variable.Type, variable.Required);
            row.PropertyChanged += OnVariableRowChanged;
            VariableRows.Add(row);
        }

        OnPropertyChanged(nameof(HasVariableRows));
        PullValuesFromJson();
    }

    private void OnVariableRowChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(GraphQlVariableRow.Value) && !_syncingVars)
        {
            PushRowsToJson();
        }
    }

    /// <summary>Reads each grid row's value from the variables JSON and recomputes the warnings.</summary>
    private void PullValuesFromJson()
    {
        var obj = TryParseVariables(VariablesJson);

        _syncingVars = true;
        try
        {
            foreach (var row in VariableRows)
            {
                row.Value = obj is not null && obj.TryGetPropertyValue(row.Name, out var node)
                    ? node?.ToJsonString() ?? "null"
                    : string.Empty;
            }
        }
        finally
        {
            _syncingVars = false;
        }

        RecomputeWarnings(obj);
    }

    /// <summary>Rebuilds the variables JSON from the grid rows (a value parses as JSON, else is a string).</summary>
    private void PushRowsToJson()
    {
        var obj = new JsonObject();

        foreach (var row in VariableRows)
        {
            if (string.IsNullOrWhiteSpace(row.Value))
            {
                continue;
            }

            JsonNode? node;
            try
            {
                node = JsonNode.Parse(row.Value);
            }
            catch (JsonException)
            {
                node = JsonValue.Create(row.Value);
            }

            obj[row.Name] = node;
        }

        _syncingVars = true;
        try
        {
            VariablesJson = obj.Count == 0 ? string.Empty : obj.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
        }
        finally
        {
            _syncingVars = false;
        }

        RecomputeWarnings(obj);
    }

    private void RecomputeWarnings(JsonObject? variables)
    {
        VariableWarnings.Clear();

        var declared = SelectedOperation?.Variables ?? [];

        foreach (var variable in declared)
        {
            var bound = variables is not null && variables.TryGetPropertyValue(variable.Name, out var node) && node is not null;

            if (variable.Required && !bound)
            {
                VariableWarnings.Add(new GraphQlProblem(
                    $"${variable.Name} ({variable.Type}) is required but not provided.", GraphQlProblemKind.Variables));
            }
        }

        if (variables is not null)
        {
            var names = declared.Select(v => v.Name).ToHashSet(StringComparer.Ordinal);

            foreach (var pair in variables)
            {
                if (!names.Contains(pair.Key))
                {
                    VariableWarnings.Add(new GraphQlProblem(
                        $"${pair.Key} is set but not declared by the operation.", GraphQlProblemKind.Variables));
                }
            }
        }

        OnPropertyChanged(nameof(HasVariableWarnings));
    }

    private static JsonObject? TryParseVariables(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            return JsonNode.Parse(json) as JsonObject;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private void ScheduleParse()
    {
        _parseCts?.Cancel();
        _parseCts?.Dispose();

        var cts = new CancellationTokenSource();
        _parseCts = cts;
        _ = DebouncedParseAsync(cts.Token);
    }

    private async Task DebouncedParseAsync(CancellationToken cancellationToken)
    {
        try
        {
            if (ParseDebounce > TimeSpan.Zero)
            {
                await Task.Delay(ParseDebounce, cancellationToken).ConfigureAwait(false);
            }

            var result = _graphql.Parse(Document);

            if (cancellationToken.IsCancellationRequested)
            {
                return;
            }

            await _dispatcher.InvokeAsync(() => ApplyParse(result)).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Superseded by a newer edit; drop silently.
        }
    }

    /// <summary>Reconciles the operation picker and Problems strip with a fresh parse (test seam).</summary>
    internal void ApplyParse(GraphQlParseResult result)
    {
        var previousName = SelectedOperation?.Name;

        Operations.Clear();

        foreach (var operation in result.Operations)
        {
            Operations.Add(operation);
        }

        // GQL-012: a single operation auto-selects; otherwise keep the prior choice if it still exists.
        SelectedOperation = Operations.Count == 1
            ? Operations[0]
            : Operations.FirstOrDefault(o => o.Name == previousName);

        Problems.Clear();

        foreach (var problem in result.Problems)
        {
            Problems.Add(problem);
        }

        OnPropertyChanged(nameof(HasProblems));
        OnPropertyChanged(nameof(HasSyntaxError));
        ExecuteCommand.NotifyCanExecuteChanged();
    }

    /// <summary>
    ///     Session-restore seam (FR-146): parse the current document synchronously and re-select the named
    ///     operation if it still exists, so a restored tab shows its picker + gating immediately rather than
    ///     waiting on the idle debounce.
    /// </summary>
    public void ReparseAndSelect(string? operationName)
    {
        ApplyParse(_graphql.Parse(Document));

        if (operationName is not null && Operations.FirstOrDefault(o => o.Name == operationName) is { } match)
        {
            SelectedOperation = match;
        }
    }

    [RelayCommand(CanExecute = nameof(CanExecute), IncludeCancelCommand = true)]
    private async Task Execute(CancellationToken cancellationToken)
    {
        // GQL-060: a subscription streams into the console instead of producing a single envelope.
        if (IsSubscription)
        {
            await StreamInternalAsync(cancellationToken);
            return;
        }

        await _dispatcher.InvokeAsync(() =>
        {
            State = RunState.InFlight;
            ResponseJson = null;
            StatusText = null;
            StatusIsError = false;
            FieldProgress.Clear();
            OnPropertyChanged(nameof(HasFieldProgress));
            VerboseLog.Clear();
            OnPropertyChanged(nameof(HasVerboseLog));
            Errors.Clear();
            OnPropertyChanged(nameof(HasErrors));
            ClearExecutionProblems();
        });

        var request = BuildRequest();
        var stopwatch = Stopwatch.StartNew();
        GraphQlExecutionResult? result = null;

        try
        {
            result = await _graphql.ExecuteAsync(request, new FieldProgressSink(this), cancellationToken);
            await _dispatcher.InvokeAsync(() => Apply(result));
        }
        catch (OperationCanceledException)
        {
            await _dispatcher.InvokeAsync(() =>
            {
                State = RunState.Cancelled;
                StatusText = "Cancelled";
                StatusIsError = true;
            });
        }

        stopwatch.Stop();
        await RecordHistoryAsync(request, result, stopwatch.ElapsedMilliseconds);
    }

    /// <summary>GQL-060..064: streams a subscription into the console, preserving received envelopes on cancel (AC-3).</summary>
    private async Task StreamInternalAsync(CancellationToken cancellationToken)
    {
        // GQL-064: a subscription is never parallelised — multiple root fields can't be streamed. Flag it
        // before execution rather than letting the bridge reject it mid-stream.
        if (SelectedOperation is { RootFieldCount: > 1 })
        {
            await _dispatcher.InvokeAsync(() =>
            {
                Problems.Add(new GraphQlProblem(
                    "A subscription must have exactly one root field — multiple fields cannot be streamed.",
                    GraphQlProblemKind.Configuration));
                OnPropertyChanged(nameof(HasProblems));
                StatusText = "Configuration error";
                StatusIsError = true;
                State = RunState.Failed;
            });
            return;
        }

        await _dispatcher.InvokeAsync(() =>
        {
            State = RunState.InFlight;
            ResponseJson = null;
            StatusText = null;
            StatusIsError = false;
            StreamLog.Reset();
            ClearExecutionProblems();
        });

        var request = BuildRequest();
        var stopwatch = Stopwatch.StartNew();
        var index = 0L;

        try
        {
            await StreamDispatchPump.RunAsync(
                _graphql.StreamAsync(request, cancellationToken),
                batch => _dispatcher.InvokeAsync(() =>
                {
                    foreach (var line in batch)
                    {
                        StreamLog.Append(new GraphQlStreamRow(index++, stopwatch.ElapsedMilliseconds, line));
                    }
                }),
                cancellationToken);

            await _dispatcher.InvokeAsync(() =>
            {
                if (State == RunState.InFlight)
                {
                    State = RunState.Completed;
                }

                StatusText = $"Completed — {StreamLog.TotalReceived} messages";
            });
        }
        catch (OperationCanceledException)
        {
            await _dispatcher.InvokeAsync(() =>
            {
                // AC-3: received envelopes stay in the console; a final status row records the cancellation.
                StreamLog.AppendStatus($"Cancelled after {StreamLog.TotalReceived} messages");
                State = RunState.Cancelled;
                StatusText = $"Cancelled after {StreamLog.TotalReceived} messages";
                StatusIsError = true;
            });
        }
    }

    /// <summary>GQL-063: export the received subscription envelopes as newline-delimited JSON.</summary>
    [RelayCommand]
    private async Task ExportStream()
    {
        if (_filePicker is null)
        {
            return;
        }

        var path = await _filePicker.SaveFileAsync("Export subscription stream", "stream.ndjson", [".ndjson", ".json"]);

        if (path is null)
        {
            return;
        }

        await using var writer = _writerFactory(path);

        foreach (var row in StreamLog.Rows)
        {
            if (!row.IsStatus)
            {
                await writer.WriteLineAsync(row.Json);
            }
        }
    }

    /// <summary>FR-120: records the execution to history, best-effort — a history hiccup never breaks Execute.</summary>
    private async Task RecordHistoryAsync(GraphQlExecutionRequest request, GraphQlExecutionResult? result, long durationMs)
    {
        if (_recorder is null)
        {
            return;
        }

        var (ok, status, category, error, envelope) = Describe(result);

        var context = new GraphQlHistoryContext(
            request.Connection,
            SelectedOperation?.Name ?? "(anonymous)",
            request.Document,
            request.Headers,
            request.Deadline,
            request.EmitDefaults,
            request.AllowUnknownFields,
            _environment?.Active?.Name,
            ok, status, category, error, durationMs, envelope);

        try
        {
            await _recorder.RecordGraphQlAsync(context);
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException)
        {
            // History is a convenience; never surface a persistence hiccup to the execute flow.
        }
    }

    /// <summary>Classifies the execution outcome for history (status text, category, error, captured envelope).</summary>
    private static (bool Ok, string Status, string Category, string? Error, string? Envelope) Describe(GraphQlExecutionResult? result)
    {
        if (result is null)
        {
            return (false, "Cancelled", "cancelled", null, null);
        }

        if (result.IsConfigurationError)
        {
            return (false, "Configuration error", "configuration", result.ConfigurationErrors[0].Message, null);
        }

        return result.Ok
            ? (true, "OK", "success", null, result.EnvelopeJson)
            : (false, "Completed with errors", "graphql-errors", null, result.EnvelopeJson);
    }

    private void Apply(GraphQlExecutionResult result)
    {
        foreach (var line in result.VerboseLog)
        {
            VerboseLog.Add(line);
        }

        OnPropertyChanged(nameof(HasVerboseLog));

        foreach (var error in result.Errors)
        {
            Errors.Add(error);
        }

        OnPropertyChanged(nameof(HasErrors));

        if (result.IsConfigurationError)
        {
            foreach (var problem in result.ConfigurationErrors)
            {
                Problems.Add(problem);
            }

            OnPropertyChanged(nameof(HasProblems));
            StatusText = "Configuration error";
            StatusIsError = true;
            State = RunState.Failed;
            return;
        }

        ResponseJson = result.EnvelopeJson;
        StatusText = result.Ok ? "OK" : "Completed with errors";
        StatusIsError = !result.Ok;
        State = result.Ok ? RunState.Completed : RunState.Failed;
    }

    private GraphQlExecutionRequest BuildRequest() => new(
        Connection,
        Document,
        SelectedOperation?.Name,
        NullIfBlank(VariablesJson),
        NullIfBlank(DefaultService),
        MappingPath: null,
        Headers.Select(h => h.ToEntry()).ToList(),
        NullIfBlank(Deadline),
        EmitDefaults,
        AllowUnknownFields,
        StrictSelection,
        Introspection,
        Raw,
        Verbosity);

    /// <summary>Upserts a per-field progress row (GQL-024); always on the UI thread via <see cref="FieldProgressSink" />.</summary>
    private void ApplyFieldProgress(GraphQlFieldProgress progress)
    {
        var row = FieldProgress.FirstOrDefault(r => r.Index == progress.Index);

        if (row is null)
        {
            row = new GraphQlFieldProgressRow(progress.Index, progress.ResponseKey);
            FieldProgress.Add(row);
            OnPropertyChanged(nameof(HasFieldProgress));
        }

        row.Apply(progress);
    }

    /// <summary>Marshals bridge progress callbacks (which may arrive on worker threads) onto the UI thread.</summary>
    private sealed class FieldProgressSink(GraphQlDocumentViewModel owner) : IProgress<GraphQlFieldProgress>
    {
        public void Report(GraphQlFieldProgress value) => owner._dispatcher.Post(() => owner.ApplyFieldProgress(value));
    }

    /// <summary>Removes transient execute-time problems (config/variable), leaving live syntax problems.</summary>
    private void ClearExecutionProblems()
    {
        for (var i = Problems.Count - 1; i >= 0; i--)
        {
            if (Problems[i].Kind != GraphQlProblemKind.Syntax)
            {
                Problems.RemoveAt(i);
            }
        }

        OnPropertyChanged(nameof(HasProblems));
    }

    [RelayCommand(CanExecute = nameof(HasResponse))]
    private async Task CopyResponse()
    {
        if (ResponseJson is not null)
        {
            await _clipboard.SetTextAsync(ResponseJson);
        }
    }

    /// <summary>GQL-028: copy the equivalent <c>gql2grpc</c> command (secrets as <c>${VAR}</c> placeholders).</summary>
    [RelayCommand]
    private async Task CopyAsCli()
    {
        var command = CliCommandBuilder.BuildGraphQlCommand(BuildRequest(), CliDialect, _tlsProfile);
        await _clipboard.SetTextAsync(command);
    }

    /// <summary>Whether file open/save/import is available (the picker is wired).</summary>
    public bool CanUseFiles => _filePicker is not null;

    /// <summary>GQL-014: load a <c>.graphql</c> document from disk (4 MiB cap).</summary>
    [RelayCommand(CanExecute = nameof(CanUseFiles))]
    private async Task OpenDocument()
    {
        if (_filePicker is null)
        {
            return;
        }

        var path = await _filePicker.OpenFileAsync("Open GraphQL document", [".graphql", ".gql"]);

        if (path is not null && await TryReadAsync(path, MaxFileBytes) is { } text)
        {
            Document = text;
        }
    }

    /// <summary>GQL-014: save the current document to a <c>.graphql</c> file.</summary>
    [RelayCommand(CanExecute = nameof(CanUseFiles))]
    private async Task SaveDocument()
    {
        if (_filePicker is null)
        {
            return;
        }

        var path = await _filePicker.SaveFileAsync("Save GraphQL document", "operation.graphql", [".graphql", ".gql"]);

        if (path is not null)
        {
            await using var writer = _writerFactory(path);
            await writer.WriteAsync(Document);
        }
    }

    /// <summary>GQL-020: import a variables <c>.json</c> file into the variables pane (4 MiB cap).</summary>
    [RelayCommand(CanExecute = nameof(CanUseFiles))]
    private async Task ImportVariables()
    {
        if (_filePicker is null)
        {
            return;
        }

        var path = await _filePicker.OpenFileAsync("Import variables", [".json"]);

        if (path is not null && await TryReadAsync(path, MaxFileBytes) is { } text)
        {
            VariablesJson = text;
        }
    }

    /// <summary>Reads a file through the cap-enforcing reader; a failure surfaces as a configuration problem.</summary>
    private async Task<string?> TryReadAsync(string path, long maxBytes)
    {
        try
        {
            return await _fileReader(path, maxBytes, CancellationToken.None);
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or UnauthorizedAccessException)
        {
            Problems.Add(new GraphQlProblem(ex.Message, GraphQlProblemKind.Configuration));
            OnPropertyChanged(nameof(HasProblems));
            return null;
        }
    }

    [RelayCommand]
    private void AddHeader() => Headers.Add(new HeaderRowViewModel());

    [RelayCommand]
    private void RemoveHeader(HeaderRowViewModel? row)
    {
        if (row is not null)
        {
            _ = Headers.Remove(row);
        }
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
        }

        OnPropertyChanged(nameof(HasHeaderErrors));
        ExecuteCommand.NotifyCanExecuteChanged();
    }

    private void OnHeaderRowChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(HeaderRowViewModel.HasBinError))
        {
            OnPropertyChanged(nameof(HasHeaderErrors));
            ExecuteCommand.NotifyCanExecuteChanged();
        }
    }

    private static string? NullIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;
}
