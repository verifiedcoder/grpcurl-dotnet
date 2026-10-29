using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GrpCurl.Net.Studio.ViewModels.Models.History;
using GrpCurl.Net.Studio.ViewModels.Panes;
using GrpCurl.Net.Studio.ViewModels.Services;
using System.Collections.ObjectModel;

namespace GrpCurl.Net.Studio.ViewModels.Documents;

/// <summary>
///     The History tab (FR-120..129): a searchable/filterable grid over the recorded invocations, with
///     pin, clear, NDJSON export, and replay-to-tab. New entries follow capture/redaction policy; legacy payloads are preserved. This view only
///     reads + manages them. When capture is disabled (FR-129) a banner is shown.
/// </summary>
public sealed partial class HistoryDocumentViewModel : DocumentViewModel, IOwnsBackgroundWork
{
    private const string All = "All";

    private readonly IHistoryStore _history;
    private readonly ISettingsStore _settings;
    private readonly IWorkspaceStore _workspace;
    private readonly IDocumentHost _host;
    private readonly IDialogService _dialogs;
    private readonly IFilePickerService? _filePicker;
    private readonly IUiDispatcher _dispatcher;
    private readonly ConsoleViewModel? _console;

    private IReadOnlyList<HistoryEntry> _all = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    public partial bool CaptureEnabled { get; set; } = true;

    [ObservableProperty]
    public partial string? HistoryMessage { get; set; }

    [ObservableProperty]
    public partial string SearchText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string ConnectionFilter { get; set; } = All;

    [ObservableProperty]
    public partial string CategoryFilter { get; set; } = All;

    [ObservableProperty]
    public partial string KindFilter { get; set; } = All;

    [ObservableProperty]
    public partial bool PinnedOnly { get; set; }

    public HistoryDocumentViewModel(
        IHistoryStore history,
        ISettingsStore settings,
        IWorkspaceStore workspace,
        IDocumentHost host,
        IDialogService dialogs,
        IUiDispatcher dispatcher,
        IFilePickerService? filePicker = null,
        ConsoleViewModel? console = null)
    {
        _history = history;
        _settings = settings;
        _workspace = workspace;
        _host = host;
        _dialogs = dialogs;
        _filePicker = filePicker;
        _dispatcher = dispatcher;
        _console = console;

        Title = "History";
        ConnectionOptions = [All];
        Track(LoadAsync());
    }

    /// <summary>History rows are this tab's children (PRD-005).</summary>
    void IOwnsBackgroundWork.CollectOwnedWork(List<Task?> tasks) => WorkGraph.CollectAll(Rows, tasks);

    public ObservableCollection<HistoryRowViewModel> Rows { get; } = [];

    public ObservableCollection<string> ConnectionOptions { get; }

    public IReadOnlyList<string> CategoryOptions { get; } =
        [All, "success", "rpc-error", "transport", "cancelled", "input", "internal"];

    public IReadOnlyList<string> KindOptions { get; } = [All, "gRPC", "GraphQL"];

    public bool IsEmpty => Rows.Count == 0;

    public async Task LoadAsync()
    {
        CaptureEnabled = _settings.Current.History.Enabled;
        IReadOnlyList<HistoryEntry> entries;
        try { entries = await _history.ReadAllAsync().ConfigureAwait(false); }
        catch (HistoryStorageException ex)
        {
            await _dispatcher.InvokeAsync(() => { HistoryMessage = ex.Message; _all = []; ApplyFilter(); });
            return;
        }

        await _dispatcher.InvokeAsync(() =>
        {
            HistoryMessage = (_history as IHistoryStatus)?.Message ?? "Request payloads require capture consent. Omitted or redacted bodies cannot be replayed.";
            _all = entries.Reverse().ToList(); // newest first

            var names = _all.Select(e => e.Connection.Name).Distinct().OrderBy(n => n, StringComparer.Ordinal);
            ConnectionOptions.Clear();
            ConnectionOptions.Add(All);
            foreach (var name in names)
            {
                ConnectionOptions.Add(name);
            }

            ApplyFilter();
        });
    }

    partial void OnSearchTextChanged(string value) => ApplyFilter();

    partial void OnConnectionFilterChanged(string value) => ApplyFilter();

    partial void OnCategoryFilterChanged(string value) => ApplyFilter();

    partial void OnKindFilterChanged(string value) => ApplyFilter();

    partial void OnPinnedOnlyChanged(bool value) => ApplyFilter();

    private void ApplyFilter()
    {
        var search = SearchText?.Trim() ?? string.Empty;

        var filtered = _all.Where(e =>
            (ConnectionFilter == All || e.Connection.Name == ConnectionFilter)
            && (CategoryFilter == All || e.Outcome.Category == CategoryFilter)
            && (KindFilter == All || KindLabel(e.Kind) == KindFilter)
            && (!PinnedOnly || e.Pinned)
            && (search.Length == 0 || Matches(e, search)));

        Rows.Clear();
        foreach (var entry in filtered)
        {
            Rows.Add(new HistoryRowViewModel(entry, IsReplayable(entry)));
        }

        OnPropertyChanged(nameof(IsEmpty));
    }

    private static bool Matches(HistoryEntry entry, string search)
        => entry.Method.Contains(search, StringComparison.OrdinalIgnoreCase)
           || entry.Connection.Name.Contains(search, StringComparison.OrdinalIgnoreCase)
           || entry.Request.Body.Contains(search, StringComparison.OrdinalIgnoreCase);

    private bool IsReplayable(HistoryEntry entry)
        => !entry.Request.BodyTruncated && entry.Request.BodyDisposition is null && HistoryReplay.ResolveConnection(_workspace.Current, entry) is not null;

    private static string KindLabel(HistoryKind kind) => kind == HistoryKind.Grpc ? "gRPC" : "GraphQL";

    [RelayCommand]
    private Task Refresh() => LoadAsync();

    /// <summary>FR-124: pin/unpin (pinned entries survive retention).</summary>
    [RelayCommand]
    private async Task TogglePin(HistoryRowViewModel? row)
    {
        if (row is not null)
        {
            if (!await TryHistoryAsync(() => _history.SetPinnedAsync(row.Id, !row.Pinned))) { return; }
            await LoadAsync();
        }
    }

    /// <summary>FR-123: replay opens a new invocation tab pre-filled, bound to the connection if it resolves.</summary>
    [RelayCommand]
    private async Task Replay(HistoryRowViewModel? row)
    {
        if (row is null)
        {
            return;
        }

        if (row.Entry.Request.BodyTruncated || row.Entry.Request.BodyDisposition is not null)
        {
            await _dialogs.ShowMessageAsync("Replay unavailable", "This entry's body was omitted, redacted, or truncated, so it cannot be replayed.");
            return;
        }

        if (HistoryReplay.ResolveConnection(_workspace.Current, row.Entry) is not { } connection)
        {
            await _dialogs.ShowMessageAsync("Connection no longer exists",
                $"The connection '{row.Entry.Connection.Name}' is not in the current workspace, so this entry cannot be replayed.");
            return;
        }

        // FR-123: restore body + headers + options. A redacted secret value can't be recovered, so its
        // header is restored by name and flagged "value required"; ${VAR} headers come back verbatim and
        // re-resolve at send time. The replay is a plain draft (not bound to a saved request).
        _host.OpenInvocation(connection, row.Entry.Method, HistoryReplay.ToPrefill(row.Entry.Request));
    }

    [RelayCommand]
    private async Task DeleteSelected()
    {
        var ids = Rows.Where(r => r.IsSelected).Select(r => r.Id).ToList();

        if (ids.Count > 0)
        {
            if (!await TryHistoryAsync(() => _history.DeleteAsync(ids))) { return; }
            await LoadAsync();
        }
    }

    [RelayCommand]
    private async Task ClearUnpinned()
    {
        if (await _dialogs.ConfirmAsync("Clear unpinned history?", "Delete every unpinned entry? This cannot be undone."))
        {
            if (!await TryHistoryAsync(() => _history.ClearAsync(keepPinned: true))) { return; }
            await LoadAsync();
        }
    }

    [RelayCommand]
    private async Task ClearAll()
    {
        if (await _dialogs.ConfirmAsync("Clear all history?", "Delete every entry, including pinned ones? This cannot be undone."))
        {
            if (!await TryHistoryAsync(() => _history.ClearAsync(keepPinned: false))) { return; }
            await LoadAsync();
        }
    }

    /// <summary>FR-128: export the currently-filtered entries to an explicitly confirmed plaintext NDJSON file.</summary>
    [RelayCommand]
    private async Task Export()
    {
        if (_filePicker is null)
        {
            return;
        }

        if (!await _dialogs.ConfirmAsync("Export plaintext history?", "The export is not encrypted and may contain sensitive historical payloads. Store and share it carefully.")) { return; }
        var path = await _filePicker.SaveFileAsync("Export history", "history.ndjson", ["ndjson", "json"]);

        if (path is not null)
        {
            var entries = Rows.Select(r => r.Entry).ToList();
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            if (!await TryHistoryAsync(() => _history.ExportAsync(path, entries))) { return; }
            RecordExportActivity($"Export history: {entries.Count} entr{(entries.Count == 1 ? "y" : "ies")}", stopwatch.Elapsed);
        }
    }

    private async Task<bool> TryHistoryAsync(Func<Task> operation)
    {
        try { await operation(); return true; }
        catch (HistoryStorageException ex) { HistoryMessage = ex.Message; return false; }
    }

    // FR-004/#14: mirror the export to the console (operation + duration; no payload is logged to the console).
    private void RecordExportActivity(string title, TimeSpan elapsed)
    {
        if (_console is null)
        {
            return;
        }

        var ms = elapsed.TotalMilliseconds;
        _console.AppendCall(new ConsoleCallActivity(
            title, 0, "written", false, $"{ms:0} ms",
            [new CallTimingPhase("export", $"{ms:0} ms", 1.0)],
            ConsoleActivityKind.Export, DateTimeOffset.UtcNow));
    }
}
