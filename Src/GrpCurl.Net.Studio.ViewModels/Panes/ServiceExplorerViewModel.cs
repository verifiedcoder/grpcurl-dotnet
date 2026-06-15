using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GrpCurl.Net.Studio.ViewModels.Explorer;
using GrpCurl.Net.Studio.ViewModels.Models.Descriptors;
using GrpCurl.Net.Studio.ViewModels.Services;

namespace GrpCurl.Net.Studio.ViewModels.Panes;

/// <summary>
///     Bottom section of the left sidebar: the reflection-backed service/method tree (FR-020..029).
///     Observes the active connection via <see cref="IConnectionSelection" />, loads its catalog
///     through <see cref="IDescriptorService" />, and presents the tree with streaming-shape badges,
///     a filter, and explicit loading/empty/error states. Descriptor work runs off the UI thread;
///     collection mutations are marshalled back through <see cref="IUiDispatcher" />.
/// </summary>
public sealed partial class ServiceExplorerViewModel : ViewModelBase
{
    private readonly IDescriptorService _descriptors;
    private readonly IConnectionSelection _selection;
    private readonly IClipboardService _clipboard;
    private readonly IUiDispatcher _dispatcher;
    private readonly IDocumentHost _documentHost;

    private ServiceCatalog? _catalog;
    private CancellationTokenSource? _loadCts;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNoConnection), nameof(IsLoading), nameof(IsLoaded), nameof(IsEmpty), nameof(HasError))]
    private ExplorerState _state = ExplorerState.NoConnection;

    [ObservableProperty]
    private string _filterText = string.Empty;

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    private string? _errorHint;

    [ObservableProperty]
    private bool _reflectionUnavailable;

    [ObservableProperty]
    private MethodNodeViewModel? _selectedMethod;

    public ServiceExplorerViewModel(
        IDescriptorService descriptors,
        IConnectionSelection selection,
        IClipboardService clipboard,
        IUiDispatcher dispatcher,
        IDocumentHost documentHost)
    {
        _descriptors = descriptors;
        _selection = selection;
        _clipboard = clipboard;
        _dispatcher = dispatcher;
        _documentHost = documentHost;

        Services = [];
        TypePackages = [];
        _selection.CurrentChanged += OnConnectionChanged;

        if (_selection.Current is not null)
        {
            _ = ReloadAsync();
        }
    }

    public string Header => "Service Explorer";

    public ObservableCollection<ServiceNodeViewModel> Services { get; }

    /// <summary>The Types branch: message/enum types grouped by package (FR-022).</summary>
    public ObservableCollection<TypePackageNodeViewModel> TypePackages { get; }

    public bool IsNoConnection => State == ExplorerState.NoConnection;
    public bool IsLoading => State == ExplorerState.Loading;
    public bool IsLoaded => State == ExplorerState.Loaded;
    public bool IsEmpty => State == ExplorerState.Empty;
    public bool HasError => State == ExplorerState.Error;

    private bool CanRefresh => _selection.Current is not null && State != ExplorerState.Loading;

    [RelayCommand(CanExecute = nameof(CanRefresh))]
    private Task Refresh() => ReloadAsync();

    [RelayCommand(CanExecute = nameof(IsLoading))]
    private void Cancel() => _loadCts?.Cancel();

    [RelayCommand]
    private async Task CopyFullName(string? fullName)
    {
        if (!string.IsNullOrWhiteSpace(fullName))
        {
            await _clipboard.SetTextAsync(fullName);
        }
    }

    /// <summary>Opens a describe tab for the symbol on the active connection (FR-024/027).</summary>
    [RelayCommand]
    private void Describe(string? symbol)
    {
        if (!string.IsNullOrWhiteSpace(symbol) && _selection.Current is { } connection)
        {
            _documentHost.OpenDescribe(connection, symbol);
        }
    }

    /// <summary>Opens a new invocation tab pre-filled from the method's template (FR-024/053).</summary>
    [RelayCommand]
    private void NewRequest(string? methodSymbol)
    {
        if (!string.IsNullOrWhiteSpace(methodSymbol) && _selection.Current is { } connection)
        {
            _documentHost.OpenInvocation(connection, methodSymbol);
        }
    }

    private void OnConnectionChanged(object? sender, EventArgs e) => _ = ReloadAsync();

    private async Task ReloadAsync()
    {
        var connection = _selection.Current;

        _loadCts?.Cancel();

        if (connection is null)
        {
            _catalog = null;
            await _dispatcher.InvokeAsync(() =>
            {
                Services.Clear();
                ClearError();
                State = ExplorerState.NoConnection;
            });
            return;
        }

        var cts = new CancellationTokenSource();
        _loadCts = cts;

        await _dispatcher.InvokeAsync(() =>
        {
            Services.Clear();
            ClearError();
            State = ExplorerState.Loading;
        });

        try
        {
            var result = await _descriptors.LoadAsync(connection, cts.Token);

            if (cts.IsCancellationRequested)
            {
                return;
            }

            await _dispatcher.InvokeAsync(() => Apply(result));
        }
        catch (OperationCanceledException)
        {
            // Superseded or user-cancelled: settle into a neutral state based on what we have.
            await _dispatcher.InvokeAsync(() =>
                State = _catalog is null
                    ? ExplorerState.NoConnection
                    : _catalog.Services.Count == 0 ? ExplorerState.Empty : ExplorerState.Loaded);
        }
    }

    private void Apply(DescriptorLoadResult result)
    {
        if (!result.Ok)
        {
            ErrorMessage = result.Error!.Message;
            ErrorHint = result.Error.Hint;
            ReflectionUnavailable = result.Error.ReflectionUnavailable;
            State = ExplorerState.Error;
            return;
        }

        _catalog = result.Catalog;
        ApplyFilter();
        State = _catalog!.Services.Count == 0 ? ExplorerState.Empty : ExplorerState.Loaded;
    }

    partial void OnFilterTextChanged(string value) => ApplyFilter();

    /// <summary>
    ///     Rebuilds the visible tree from the loaded catalog applying the case-insensitive filter
    ///     over fully-qualified service and method names (FR-023). Matching services auto-expand so
    ///     matched methods are visible; non-matching branches are pruned.
    /// </summary>
    private void ApplyFilter()
    {
        Services.Clear();
        TypePackages.Clear();

        if (_catalog is null)
        {
            return;
        }

        var filter = FilterText?.Trim() ?? string.Empty;
        var filtering = filter.Length > 0;

        foreach (var service in _catalog.Services)
        {
            var serviceMatches = !filtering || service.FullName.Contains(filter, StringComparison.OrdinalIgnoreCase);

            var methods = service.Methods
                .Where(m => serviceMatches
                    || m.Name.Contains(filter, StringComparison.OrdinalIgnoreCase)
                    || m.FullName.Contains(filter, StringComparison.OrdinalIgnoreCase))
                .Select(m => new MethodNodeViewModel(m, CopyFullNameCommand, DescribeCommand, NewRequestCommand))
                .ToList();

            if (methods.Count == 0 && !serviceMatches)
            {
                continue;
            }

            Services.Add(new ServiceNodeViewModel(service.FullName, methods, CopyFullNameCommand, DescribeCommand) { IsExpanded = filtering });
        }

        // Types branch (FR-022): message/enum types grouped by package, filtered by FQN.
        var matchingTypes = _catalog.Types
            .Where(t => !filtering || t.FullName.Contains(filter, StringComparison.OrdinalIgnoreCase));

        foreach (var group in matchingTypes.GroupBy(t => t.Package).OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            var leaves = group
                .OrderBy(t => t.FullName, StringComparer.Ordinal)
                .Select(t => new TypeLeafNodeViewModel(t, DescribeCommand, CopyFullNameCommand))
                .ToList();

            var packageName = string.IsNullOrEmpty(group.Key) ? "(default)" : group.Key;
            TypePackages.Add(new TypePackageNodeViewModel(packageName, leaves) { IsExpanded = filtering });
        }
    }

    private void ClearError()
    {
        ErrorMessage = null;
        ErrorHint = null;
        ReflectionUnavailable = false;
    }
}
