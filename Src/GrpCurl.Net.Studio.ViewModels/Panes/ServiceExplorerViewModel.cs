using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GrpCurl.Net.Studio.ViewModels.Explorer;
using GrpCurl.Net.Studio.ViewModels.Models.Connections;
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
    private readonly IProtocService? _protoc;
    private readonly ConsoleViewModel? _console;

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

    /// <summary>The active descriptor source kind, e.g. "Server reflection" / "Protoset" / "Proto (protoc)" (FR-040/048).</summary>
    [ObservableProperty]
    private string? _sourceKind;

    /// <summary>One-line load metadata: file/symbol counts + load duration (FR-048).</summary>
    [ObservableProperty]
    private string? _sourceSummary;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LastRefreshedText))]
    private DateTimeOffset? _lastRefreshed;

    /// <summary>FR-043: the protoc binary in use (resolved path + version), shown when the source is Proto.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasProtocDetail))]
    private string? _protocDetail;

    public ServiceExplorerViewModel(
        IDescriptorService descriptors,
        IConnectionSelection selection,
        IClipboardService clipboard,
        IUiDispatcher dispatcher,
        IDocumentHost documentHost,
        IProtocService? protoc = null,
        ConsoleViewModel? console = null)
    {
        _descriptors = descriptors;
        _selection = selection;
        _clipboard = clipboard;
        _dispatcher = dispatcher;
        _documentHost = documentHost;
        _protoc = protoc;
        _console = console;

        Services = [];
        TypePackages = [];
        Warnings = [];
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

    /// <summary>Non-fatal descriptor-load warnings (FR-046); also mirrored to the console.</summary>
    public ObservableCollection<string> Warnings { get; }

    public int WarningCount => Warnings.Count;

    public bool HasWarnings => Warnings.Count > 0;

    public string? LastRefreshedText => LastRefreshed is { } t ? $"Refreshed {t:HH:mm:ss}" : null;

    public bool HasProtocDetail => ProtocDetail is not null;

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
                ClearSourceMetadata();
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
            ClearSourceMetadata();
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
        ApplySourceMetadata(_catalog!);
        State = _catalog!.Services.Count == 0 ? ExplorerState.Empty : ExplorerState.Loaded;
    }

    /// <summary>Populates the explorer-header source badge + warnings strip from a loaded catalog (FR-040/043/046/048).</summary>
    private void ApplySourceMetadata(ServiceCatalog catalog)
    {
        var mode = _selection.Current?.DescriptorSource.Mode ?? DescriptorMode.Reflection;
        SourceKind = mode switch
        {
            DescriptorMode.Protoset => "Protoset",
            DescriptorMode.Proto => "Proto (protoc)",
            _ => "Server reflection"
        };
        SourceSummary = $"{catalog.FileCount} file(s) · {catalog.SymbolCount} symbol(s) · {catalog.LoadDuration.TotalMilliseconds:0} ms";
        LastRefreshed = DateTimeOffset.Now;

        Warnings.Clear();
        foreach (var warning in catalog.Warnings)
        {
            Warnings.Add(warning);
            _console?.Append($"[descriptor] {warning}"); // FR-046 mirror to the bottom console
        }

        OnPropertyChanged(nameof(WarningCount));
        OnPropertyChanged(nameof(HasWarnings));

        // FR-043: surface the protoc binary in use for proto sources.
        ProtocDetail = null;
        if (mode == DescriptorMode.Proto)
        {
            _ = UpdateProtocDetailAsync();
        }
    }

    private async Task UpdateProtocDetailAsync()
    {
        if (_protoc is null)
        {
            return;
        }

        var info = await _protoc.DetectAsync();
        await _dispatcher.InvokeAsync(() => ProtocDetail = info.Found ? $"protoc: {info.Message}" : null);
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

    private void ClearSourceMetadata()
    {
        SourceKind = null;
        SourceSummary = null;
        LastRefreshed = null;
        ProtocDetail = null;
        Warnings.Clear();
        OnPropertyChanged(nameof(WarningCount));
        OnPropertyChanged(nameof(HasWarnings));
    }
}
