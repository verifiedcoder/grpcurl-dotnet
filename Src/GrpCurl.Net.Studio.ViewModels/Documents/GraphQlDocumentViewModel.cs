using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GrpCurl.Net.Studio.ViewModels.Connections;
using GrpCurl.Net.Studio.ViewModels.Models.Connections;
using GrpCurl.Net.Studio.ViewModels.Models.GraphQl;
using GrpCurl.Net.Studio.ViewModels.Services;
using System.Collections.ObjectModel;
using System.ComponentModel;

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
    private readonly IGraphQlService _graphql;
    private readonly IUiDispatcher _dispatcher;
    private readonly IClipboardService _clipboard;
    private CancellationTokenSource? _parseCts;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsInFlight), nameof(IsCompleted), nameof(HasResponse))]
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
        IClipboardService clipboard)
    {
        Connection = connection;
        _graphql = graphql;
        _dispatcher = dispatcher;
        _clipboard = clipboard;
        Title = "GraphQL";

        Headers.CollectionChanged += OnHeadersChanged;
    }

    public SavedConnection Connection { get; }

    public override SavedConnection? TabConnection => Connection;

    /// <summary>The operations found in the current document (drives the picker, GQL-012).</summary>
    public ObservableCollection<GraphQlOperationInfo> Operations { get; } = [];

    /// <summary>Request metadata headers (GQL-003) — shared editor with invocation tabs.</summary>
    public ObservableCollection<HeaderRowViewModel> Headers { get; } = [];

    /// <summary>Syntax problems (GQL-011) and pre-RPC configuration/variable errors (AC-5); blocks Execute when any are syntax.</summary>
    public ObservableCollection<GraphQlProblem> Problems { get; } = [];

    /// <summary>Debounce before the document is re-parsed after an edit; tests shorten it.</summary>
    internal TimeSpan ParseDebounce { get; set; } = TimeSpan.FromMilliseconds(300);

    public bool IsInFlight => State == RunState.InFlight;
    public bool IsCompleted => State is RunState.Completed or RunState.Failed or RunState.Cancelled;
    public bool HasResponse => ResponseJson is not null;
    public bool HasProblems => Problems.Count > 0;

    /// <summary>GQL-067 parity: any header with an invalid <c>-bin</c> value blocks execution.</summary>
    public bool HasHeaderErrors => Headers.Any(h => h.HasBinError);

    /// <summary>A syntax problem makes the document unexecutable (GQL-011); a chosen operation is required (GQL-012).</summary>
    public bool HasSyntaxError => Problems.Any(p => p.Kind == GraphQlProblemKind.Syntax);

    private bool CanExecute => State != RunState.InFlight && SelectedOperation is not null && !HasSyntaxError && !HasHeaderErrors;

    partial void OnDocumentChanged(string value) => ScheduleParse();

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

    [RelayCommand(CanExecute = nameof(CanExecute), IncludeCancelCommand = true)]
    private async Task Execute(CancellationToken cancellationToken)
    {
        await _dispatcher.InvokeAsync(() =>
        {
            State = RunState.InFlight;
            ResponseJson = null;
            StatusText = null;
            StatusIsError = false;
            ClearExecutionProblems();
        });

        var request = BuildRequest();

        try
        {
            var result = await _graphql.ExecuteAsync(request, cancellationToken);
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
    }

    private void Apply(GraphQlExecutionResult result)
    {
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
        Raw: false);

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
