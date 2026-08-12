using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GrpCurl.Net.Studio.ViewModels.Models.Connections;
using GrpCurl.Net.Studio.ViewModels.Models.Descriptors;
using GrpCurl.Net.Studio.ViewModels.Services;

namespace GrpCurl.Net.Studio.ViewModels.Documents;

/// <summary>
///     A describe tab (FR-050): renders a symbol's structured description and supports FR-051
///     navigation — clickable type references load in-tab with back/forward history, and Ctrl+click
///     opens the target in a new tab via the document host. Messages/methods expose the generated
///     request template (FR-052) with copy (FR-056).
/// </summary>
public sealed partial class DescribeDocumentViewModel : DocumentViewModel, IDisposable, IDrainableDocument
{
    private readonly IDescriptorService _descriptors;
    private readonly IUiDispatcher _dispatcher;
    private readonly IClipboardService _clipboard;
    private readonly IDocumentHost _host;

    private readonly Stack<string> _back = new();
    private readonly Stack<string> _forward = new();
    private CancellationTokenSource? _loadCts;

    /// <summary>
    ///     The load started by the constructor or the most recent navigation. Held only so shutdown can
    ///     wait for it: the loads are fire-and-forget by design (navigation must not block the UI), and
    ///     nothing else needs the task.
    /// </summary>
    private Task? _load;

    // int rather than bool: shutdown and the close flow can both reach Dispose, so the guard has to be
    // atomic to be worth anything (PRD-005 re-review, finding 4).
    private int _disposed;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsLoading), nameof(IsLoaded), nameof(HasError))]
    [NotifyCanExecuteChangedFor(nameof(CopyProtoCommand))]
    public partial DescribeState State { get; set; } = DescribeState.Loading;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsMethod))]
    public partial SymbolDescription? Symbol { get; set; }

    [ObservableProperty]
    public partial string? ErrorMessage { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasTemplate))]
    [NotifyCanExecuteChangedFor(nameof(CopyTemplateJsonCommand))]
    public partial string? TemplateJson { get; set; }

    public DescribeDocumentViewModel(
        SavedConnection connection,
        string symbol,
        IDescriptorService descriptors,
        IUiDispatcher dispatcher,
        IClipboardService clipboard,
        IDocumentHost host)
    {
        Connection = connection;
        CurrentSymbol = symbol;
        _descriptors = descriptors;
        _dispatcher = dispatcher;
        _clipboard = clipboard;
        _host = host;

        Title = ShortName(symbol);
        StartLoad(symbol);
    }

    public SavedConnection Connection { get; }

    public override SavedConnection? TabConnection => Connection;

    /// <summary>The symbol currently shown (changes as the user navigates type links).</summary>
    public string CurrentSymbol { get; private set; }

    public bool IsLoading => State == DescribeState.Loading;
    public bool IsLoaded => State == DescribeState.Loaded;
    public bool HasError => State == DescribeState.Error;
    public bool HasTemplate => TemplateJson is not null;

    /// <summary>True when a method is shown, enabling "Generate request" (FR-053).</summary>
    public bool IsMethod => Symbol is MethodDescription;

    private bool CanGoBack => _back.Count > 0;
    private bool CanGoForward => _forward.Count > 0;

    /// <summary>Navigates a resolvable type reference in-tab, recording history (FR-051).</summary>
    [RelayCommand]
    private void Navigate(TypeRef? typeRef)
    {
        if (typeRef is not { Resolvable: true })
        {
            return;
        }

        _back.Push(CurrentSymbol);
        _forward.Clear();
        StartLoad(typeRef.FullName);
    }

    /// <summary>Ctrl+click: opens a resolvable type reference in a new tab (FR-051).</summary>
    [RelayCommand]
    private void OpenInNewTab(TypeRef? typeRef)
    {
        if (typeRef is { Resolvable: true })
        {
            _host.OpenDescribe(Connection, typeRef.FullName, newTab: true);
        }
    }

    /// <summary>Opens a new invocation tab pre-filled with this method's request template (FR-053).</summary>
    [RelayCommand]
    private void GenerateRequest()
    {
        if (Symbol is MethodDescription method)
        {
            _host.OpenInvocation(Connection, method.FullName, method.TemplateJson);
        }
    }

    [RelayCommand(CanExecute = nameof(CanGoBack))]
    private void Back()
    {
        _forward.Push(CurrentSymbol);
        StartLoad(_back.Pop());
    }

    [RelayCommand(CanExecute = nameof(CanGoForward))]
    private void Forward()
    {
        _back.Push(CurrentSymbol);
        StartLoad(_forward.Pop());
    }

    [RelayCommand(CanExecute = nameof(HasTemplate))]
    private async Task CopyTemplateJson()
    {
        if (TemplateJson is not null)
        {
            await _clipboard.SetTextAsync(TemplateJson);
        }
    }

    /// <summary>FR-054: copies the reconstructed <c>.proto</c> of the current symbol's defining file.</summary>
    [RelayCommand(CanExecute = nameof(IsLoaded))]
    private async Task CopyProto()
    {
        var snippet = await _descriptors.GetProtoSnippetAsync(Connection, CurrentSymbol);

        if (!string.IsNullOrEmpty(snippet))
        {
            await _clipboard.SetTextAsync(snippet);
        }
    }

    /// <summary>
    ///     Starts a load without waiting for it, keeping the task so <see cref="CancelAndDrainAsync" />
    ///     can. Every navigation path goes through here rather than discarding the task with <c>_ =</c>.
    /// </summary>
    private void StartLoad(string symbol) => _load = LoadAsync(symbol);

    private async Task LoadAsync(string symbol)
    {
        _loadCts?.Cancel();
        var cts = new CancellationTokenSource();
        _loadCts = cts;

        CurrentSymbol = symbol;

        await _dispatcher.InvokeAsync(() =>
        {
            State = DescribeState.Loading;
            Title = ShortName(symbol);
            NotifyNavigationChanged();
        });

        try
        {
            var result = await _descriptors.DescribeAsync(Connection, symbol, cts.Token);

            if (cts.IsCancellationRequested)
            {
                return;
            }

            await _dispatcher.InvokeAsync(() => Apply(result));
        }
        catch (OperationCanceledException)
        {
            // superseded by a newer navigation — drop quietly
        }
    }

    private void Apply(DescribeResult result)
    {
        if (!result.Ok)
        {
            ErrorMessage = result.Error!.Message;
            Symbol = null;
            TemplateJson = null;
            State = DescribeState.Error;
            return;
        }

        Symbol = result.Symbol;
        TemplateJson = result.Symbol switch
        {
            MessageDescription m => m.TemplateJson,
            MethodDescription d  => d.TemplateJson,
            _                    => null
        };
        ErrorMessage = null;
        State = DescribeState.Loaded;
    }

    private void NotifyNavigationChanged()
    {
        BackCommand.NotifyCanExecuteChanged();
        ForwardCommand.NotifyCanExecuteChanged();
    }

    private static string ShortName(string symbol)
    {
        var trimmed = symbol.TrimEnd('.', '/');
        var lastDot = trimmed.LastIndexOfAny(['.', '/']);
        return lastDot >= 0 && lastDot < trimmed.Length - 1 ? trimmed[(lastDot + 1)..] : trimmed;
    }

    /// <summary>
    ///     Cancels and releases the in-flight describe load when the tab closes (PRD-005). Idempotent
    ///     and non-throwing.
    ///     <para>
    ///         <c>LoadAsync</c> cancels the previous token source on each navigation but never disposes
    ///         one, so closing a tab mid-load left the lookup running against a descriptor service the
    ///         user had walked away from.
    ///     </para>
    /// </summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _loadCts?.Cancel();
        _loadCts?.Dispose();
        _loadCts = null;
    }

    /// <summary>
    ///     Cancels the in-flight describe load and returns a task that completes when it has unwound
    ///     (PRD-005 re-review, finding 1). The load is not owned by a command, so its token source is
    ///     cancelled directly; the task itself is the one <see cref="StartLoad" /> kept.
    /// </summary>
    public Task CancelAndDrainAsync()
    {
        _loadCts?.Cancel();

        return DocumentDrain.WhenSettled(_load);
    }
}
