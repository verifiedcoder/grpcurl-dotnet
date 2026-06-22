using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GrpCurl.Net.Studio.ViewModels.Explorer;
using GrpCurl.Net.Studio.ViewModels.Models.Connections;
using GrpCurl.Net.Studio.ViewModels.Models.Descriptors;
using GrpCurl.Net.Studio.ViewModels.Services;
using System.Collections.ObjectModel;

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
    private readonly IFilePickerService? _filePicker;
    private readonly IDialogService? _dialog;
    private readonly ILauncherService? _launcher;
    private readonly IInspector? _inspector;

    private ServiceCatalog? _catalog;
    private CancellationTokenSource? _loadCts;
    private string? _loadedConnectionId;
    private ExplorerTreeState? _pendingRestore;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNoConnection), nameof(IsLoading), nameof(IsLoaded), nameof(IsEmpty), nameof(HasError))]
    [NotifyCanExecuteChangedFor(nameof(ExportProtosetCommand), nameof(ExportProtosCommand))]
    public partial ExplorerState State { get; set; } = ExplorerState.NoConnection;

    [ObservableProperty]
    public partial string FilterText { get; set; } = string.Empty;

    /// <summary>FR-029: false = descriptor (file) order; true = A→Z by name.</summary>
    [ObservableProperty]
    public partial bool SortAlphabetically { get; set; }

    [ObservableProperty]
    public partial string? ErrorMessage { get; set; }

    [ObservableProperty]
    public partial string? ErrorHint { get; set; }

    [ObservableProperty]
    public partial bool ReflectionUnavailable { get; set; }

    [ObservableProperty]
    public partial MethodNodeViewModel? SelectedMethod { get; set; }

    /// <summary>
    ///     The tree's current selection (bound to the Services <c>TreeView.SelectedItem</c>). A method
    ///     leaf publishes its signature to the inspector (FR-020); branch nodes leave it unchanged.
    /// </summary>
    [ObservableProperty]
    public partial object? SelectedNode { get; set; }

    /// <summary>The active descriptor source kind, e.g. "Server reflection" / "Protoset" / "Proto (protoc)" (FR-040/048).</summary>
    [ObservableProperty]
    public partial string? SourceKind { get; set; }

    /// <summary>One-line load metadata: file/symbol counts + load duration (FR-048).</summary>
    [ObservableProperty]
    public partial string? SourceSummary { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LastRefreshedText))]
    public partial DateTimeOffset? LastRefreshed { get; set; }

    /// <summary>FR-043: the protoc binary in use (resolved path + version), shown when the source is Proto.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasProtocDetail))]
    public partial string? ProtocDetail { get; set; }

    public ServiceExplorerViewModel(
        IDescriptorService descriptors,
        IConnectionSelection selection,
        IClipboardService clipboard,
        IUiDispatcher dispatcher,
        IDocumentHost documentHost,
        IProtocService? protoc = null,
        ConsoleViewModel? console = null,
        IFilePickerService? filePicker = null,
        IDialogService? dialog = null,
        ILauncherService? launcher = null,
        IInspector? inspector = null)
    {
        _descriptors = descriptors;
        _selection = selection;
        _clipboard = clipboard;
        _dispatcher = dispatcher;
        _documentHost = documentHost;
        _protoc = protoc;
        _console = console;
        _filePicker = filePicker;
        _dialog = dialog;
        _launcher = launcher;
        _inspector = inspector;

        Services = [];
        TypePackages = [];
        Warnings = [];
        _selection.CurrentChanged += OnConnectionChanged;

        if (_selection.Current is not null)
        {
            _ = ReloadAsync();
        }
    }

    public static string Header => "Service Explorer";

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

    /// <summary>SPEC-020 §5 (Ctrl+T): open a new invocation tab on the method currently selected in the
    /// tree. A no-op when no method is selected (e.g. a service/type node, or nothing).</summary>
    [RelayCommand]
    private void NewRequestForSelected() => NewRequest(SelectedMethod?.FullName);

    // ── schema export (FR-100..104) ──────────────────────────────────────────

    /// <summary>Export is offered once a schema is loaded and the file picker + dialog services are wired.</summary>
    public bool CanExport => IsLoaded && _filePicker is not null && _dialog is not null && _selection.Current is not null;

    [RelayCommand(CanExecute = nameof(CanExport), IncludeCancelCommand = true)]
    private async Task ExportProtoset(CancellationToken cancellationToken)
    {
        if (_selection.Current is not { } connection || _filePicker is null)
        {
            return;
        }

        var path = await _filePicker.SaveFileAsync("Export protoset", $"{SuggestedName(connection)}.protoset", ["protoset"], cancellationToken);

        if (path is null)
        {
            return;
        }

        await RunExportAsync(
            overwrite => _descriptors.ExportProtosetAsync(connection, path, overwrite, cancellationToken),
            revealTarget: System.IO.Path.GetDirectoryName(path) ?? path);
    }

    [RelayCommand(CanExecute = nameof(CanExport), IncludeCancelCommand = true)]
    private async Task ExportProtos(CancellationToken cancellationToken)
    {
        if (_selection.Current is not { } connection || _filePicker is null)
        {
            return;
        }

        var directory = await _filePicker.OpenFolderAsync("Reconstruct .proto files into…", cancellationToken);

        if (directory is null)
        {
            return;
        }

        await RunExportAsync(
            overwrite => _descriptors.ExportProtosAsync(connection, directory, overwrite, cancellationToken),
            revealTarget: directory);
    }

    /// <summary>
    ///     Runs an export, gating an overwrite behind a refuse-by-default confirmation (FR-101/102) and
    ///     presenting the result summary with a reveal-in-file-manager affordance (FR-103).
    /// </summary>
    private async Task RunExportAsync(Func<bool, Task<SchemaExportResult>> export, string revealTarget)
    {
        if (_dialog is null)
        {
            return;
        }

        try
        {
            var result = await export(false);

            if (result.Outcome == SchemaExportOutcome.Conflict)
            {
                if (!await _dialog.ConfirmAsync("Overwrite existing files?", ConflictMessage(result.Conflicts)))
                {
                    return;
                }

                result = await export(true);
            }

            await PresentResultAsync(result, revealTarget);
        }
        catch (OperationCanceledException)
        {
            await _dialog.ShowMessageAsync("Export cancelled", "The export was cancelled; any files already written were kept.");
        }
    }

    private async Task PresentResultAsync(SchemaExportResult result, string revealTarget)
    {
        if (_dialog is null)
        {
            return;
        }

        if (result.Outcome == SchemaExportOutcome.Failure)
        {
            await _dialog.ShowMessageAsync("Export failed", result.ErrorMessage ?? "The schema could not be exported.");
            return;
        }

        var summary = $"{result.Written.Count} file(s) written in {result.Duration.TotalMilliseconds:0} ms.";

        // The dialog service is yes/no, so the reveal affordance rides on the confirmation (FR-103).
        if (await _dialog.ConfirmAsync("Export complete", summary + "\n\nReveal in the file manager?") && _launcher is not null)
        {
            _ = await _launcher.LaunchUriAsync(new Uri(revealTarget).AbsoluteUri);
        }
    }

    private static string ConflictMessage(IReadOnlyList<FileConflict> conflicts)
    {
        var lines = conflicts.Take(10).Select(c => $"• {c.Path}  ({c.SizeBytes} bytes, modified {c.ModifiedUtc:yyyy-MM-dd HH:mm} UTC)");
        var more = conflicts.Count > 10 ? $"\n…and {conflicts.Count - 10} more" : string.Empty;
        return $"{conflicts.Count} file(s) already exist and will be overwritten:\n\n{string.Join("\n", lines)}{more}";
    }

    private static string SuggestedName(Models.Connections.SavedConnection connection)
    {
        var name = string.IsNullOrWhiteSpace(connection.Name) ? "schema" : connection.Name.Trim();
        return string.Join("_", name.Split(System.IO.Path.GetInvalidFileNameChars()));
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

        // FR-028: a refresh of the SAME connection preserves the tree's expansion + selection;
        // switching to a different connection starts fresh.
        _pendingRestore = connection.Id == _loadedConnectionId ? CaptureTreeState() : null;

        await _dispatcher.InvokeAsync(() =>
        {
            Services.Clear();
            ClearError();
            ClearSourceMetadata();
            State = ExplorerState.Loading;
        });

        try
        {
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            var result = await _descriptors.LoadAsync(connection, cts.Token);
            stopwatch.Stop();

            if (cts.IsCancellationRequested)
            {
                return;
            }

            await _dispatcher.InvokeAsync(() =>
            {
                Apply(result);
                RecordDescriptorActivity(connection, result, stopwatch.Elapsed);
            });
        }
        catch (OperationCanceledException)
        {
            // Superseded or user-cancelled: settle into a neutral state based on what we have.
            _ = await _dispatcher.InvokeAsync(() =>
                State = _catalog is null
                    ? ExplorerState.NoConnection
                    : _catalog.Services.Count == 0 ? ExplorerState.Empty : ExplorerState.Loaded);
        }
    }

    /// <summary>FR-004: log a descriptor load/refresh to the console with its outcome and total duration.</summary>
    private void RecordDescriptorActivity(SavedConnection connection, DescriptorLoadResult result, TimeSpan elapsed)
    {
        if (_console is null)
        {
            return;
        }

        var ms = elapsed.TotalMilliseconds;
        var outcome = result.Ok
            ? result.Catalog is { } catalog ? $"{catalog.Services.Count} service(s)" : "loaded"
            : "failed";

        _console.AppendCall(new ConsoleCallActivity(
            $"Describe: {connection.Name}", result.Ok ? 0 : 1, outcome, !result.Ok, $"{ms:0} ms",
            [new CallTimingPhase("descriptor", $"{ms:0} ms", 1.0)],
            ConsoleActivityKind.Descriptor, DateTimeOffset.UtcNow));
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

        _loadedConnectionId = _selection.Current?.Id;
        RestoreTreeState(); // FR-028: re-apply expansion/selection captured before the reload
    }

    /// <summary>FR-028: snapshots the current tree expansion + selected method before a refresh.</summary>
    private ExplorerTreeState CaptureTreeState() => new(
        Services.Where(s => s.IsExpanded).Select(s => s.FullName).ToHashSet(StringComparer.Ordinal),
        TypePackages.Where(p => p.IsExpanded).Select(p => p.Package).ToHashSet(StringComparer.Ordinal),
        SelectedMethod?.FullName);

    /// <summary>FR-028: restores a captured snapshot onto the freshly-rebuilt tree (by node identity).</summary>
    private void RestoreTreeState()
    {
        if (_pendingRestore is not { } state)
        {
            return;
        }

        _pendingRestore = null;

        foreach (var service in Services)
        {
            service.IsExpanded = state.ExpandedServices.Contains(service.FullName);
        }

        foreach (var package in TypePackages)
        {
            package.IsExpanded = state.ExpandedPackages.Contains(package.Package);
        }

        if (state.SelectedMethod is { } symbol)
        {
            var match = Services.SelectMany(s => s.Methods).FirstOrDefault(m => m.FullName == symbol);

            if (match is not null)
            {
                SelectedNode = match;
            }
        }
    }

    private sealed record ExplorerTreeState(
        HashSet<string> ExpandedServices,
        HashSet<string> ExpandedPackages,
        string? SelectedMethod);

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
        _ = await _dispatcher.InvokeAsync(() => ProtocDetail = info.Found ? $"protoc: {info.Message}" : null);
    }

    /// <summary>FR-020: surface a selected method's signature in the inspector.</summary>
    partial void OnSelectedNodeChanged(object? value)
    {
        if (value is MethodNodeViewModel node)
        {
            SelectedMethod = node;
            var method = node.Method;
            _inspector?.ShowMethod(new MethodSignatureContent(
                method.FullName, method.Name, node.ShapeLabel, method.InputType, method.OutputType));
        }
    }

    partial void OnFilterTextChanged(string value) => ApplyFilter();

    partial void OnSortAlphabeticallyChanged(bool value) => ApplyFilter();

    /// <summary>FR-054: copies the reconstructed <c>.proto</c> of the symbol's defining file to the clipboard.</summary>
    [RelayCommand]
    private async Task CopyProto(string? symbol)
    {
        if (string.IsNullOrWhiteSpace(symbol) || _selection.Current is not { } connection)
        {
            return;
        }

        var snippet = await _descriptors.GetProtoSnippetAsync(connection, symbol);

        if (!string.IsNullOrEmpty(snippet))
        {
            await _clipboard.SetTextAsync(snippet);
        }
    }

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

        // FR-029: descriptor (file) order by default; A→Z by name when the sort toggle is on.
        var orderedServices = SortAlphabetically
            ? _catalog.Services.OrderBy(s => s.FullName, StringComparer.Ordinal)
            : _catalog.Services.AsEnumerable();

        foreach (var service in orderedServices)
        {
            var serviceMatches = !filtering || service.FullName.Contains(filter, StringComparison.OrdinalIgnoreCase);

            var matched = service.Methods
                .Where(m => serviceMatches
                    || m.Name.Contains(filter, StringComparison.OrdinalIgnoreCase)
                    || m.FullName.Contains(filter, StringComparison.OrdinalIgnoreCase));

            var methods = (SortAlphabetically ? matched.OrderBy(m => m.Name, StringComparer.Ordinal) : matched)
                .Select(m => new MethodNodeViewModel(m, CopyFullNameCommand, DescribeCommand, NewRequestCommand, CopyProtoCommand))
                .ToList();

            if (methods.Count == 0 && !serviceMatches)
            {
                continue;
            }

            Services.Add(new ServiceNodeViewModel(service.FullName, methods, CopyFullNameCommand, DescribeCommand, CopyProtoCommand, service.Deprecated) { IsExpanded = filtering });
        }

        // Types branch (FR-022): message/enum types grouped by package, filtered by FQN.
        var matchingTypes = _catalog.Types
            .Where(t => !filtering || t.FullName.Contains(filter, StringComparison.OrdinalIgnoreCase));

        foreach (var group in matchingTypes.GroupBy(t => t.Package).OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            var leaves = group
                .OrderBy(t => t.FullName, StringComparer.Ordinal)
                .Select(t => new TypeLeafNodeViewModel(t, DescribeCommand, CopyFullNameCommand, CopyProtoCommand))
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
