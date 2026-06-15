using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GrpCurl.Net.Studio.ViewModels.Connections;
using GrpCurl.Net.Studio.ViewModels.Models.Connections;
using GrpCurl.Net.Studio.ViewModels.Models.Descriptors;
using GrpCurl.Net.Studio.ViewModels.Models.Invocation;
using GrpCurl.Net.Studio.ViewModels.Services;

namespace GrpCurl.Net.Studio.ViewModels.Documents;

/// <summary>
///     A unary invocation tab (FR-060..079): binds one connection + method, edits a JSON request,
///     runs it through <see cref="IInvocationRunner" /> (one CTS per tab via the cancel command,
///     ADR-014), and renders the response body + headers/trailers/status/timing. Off-thread work is
///     marshalled back through <see cref="IUiDispatcher" />. Rich error rendering is E1.5.
/// </summary>
public sealed partial class InvocationDocumentViewModel : DocumentViewModel
{
    private readonly IInvocationRunner _runner;
    private readonly IDescriptorService _descriptors;
    private readonly IUiDispatcher _dispatcher;
    private readonly IClipboardService _clipboard;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsInFlight), nameof(IsCompleted), nameof(HasResponse))]
    [NotifyCanExecuteChangedFor(nameof(InvokeCommand))]
    private RunState _state = RunState.Idle;

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
        IClipboardService clipboard)
    {
        Connection = connection;
        MethodSymbol = methodSymbol;
        _runner = runner;
        _descriptors = descriptors;
        _dispatcher = dispatcher;
        _clipboard = clipboard;

        Title = ShortName(methodSymbol);

        if (initialRequestJson is not null)
        {
            RequestJson = initialRequestJson;
        }
        else
        {
            _ = LoadTemplateAsync();
        }
    }

    public SavedConnection Connection { get; }

    public string MethodSymbol { get; }

    public ObservableCollection<HeaderRowViewModel> Headers { get; } = [];
    public ObservableCollection<MetadataItem> ResponseHeaders { get; } = [];
    public ObservableCollection<MetadataItem> ResponseTrailers { get; } = [];
    public ObservableCollection<TimingPhase> Timing { get; } = [];

    public bool IsInFlight => State == RunState.InFlight;
    public bool IsCompleted => State is RunState.Completed or RunState.Failed or RunState.Cancelled;
    public bool HasResponse => ResponseJson is not null;

    private bool CanInvoke => State != RunState.InFlight;

    [RelayCommand(CanExecute = nameof(CanInvoke), IncludeCancelCommand = true)]
    private async Task Invoke(CancellationToken cancellationToken)
    {
        await _dispatcher.InvokeAsync(() =>
        {
            State = RunState.InFlight;
            ResponseJson = null;
            StatusText = null;
            StatusIsError = false;
            ResponseHeaders.Clear();
            ResponseTrailers.Clear();
            Timing.Clear();
        });

        var request = new InvocationRequestModel(
            Connection,
            MethodSymbol,
            RequestJson,
            Headers.Select(h => h.ToEntry()).ToList(),
            NullIfBlank(Deadline),
            EmitDefaults,
            AllowUnknownFields,
            NullIfBlank(MaxMessageSize));

        try
        {
            var result = await _runner.InvokeUnaryAsync(request, cancellationToken);
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

        foreach (var phase in result.Timing.Phases)
        {
            Timing.Add(phase);
        }

        StatusIsError = !result.Ok;
        StatusText = result.Ok
            ? result.Status.CodeName
            : $"{result.Status.CodeName}: {result.ErrorMessage}";
        State = result.Ok ? RunState.Completed : RunState.Failed;
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
            Headers.Remove(row);
        }
    }

    private async Task LoadTemplateAsync()
    {
        var result = await _descriptors.DescribeAsync(Connection, MethodSymbol, CancellationToken.None);

        await _dispatcher.InvokeAsync(() =>
        {
            if (result is { Ok: true, Symbol: MethodDescription method })
            {
                RequestJson = method.TemplateJson;
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
