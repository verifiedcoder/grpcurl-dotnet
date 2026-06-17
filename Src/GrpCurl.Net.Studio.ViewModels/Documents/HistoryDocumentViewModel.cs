using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Globalization;
using GrpCurl.Net.Studio.ViewModels.Models.Connections;
using GrpCurl.Net.Studio.ViewModels.Models.History;
using GrpCurl.Net.Studio.ViewModels.Models.Invocation;
using GrpCurl.Net.Studio.ViewModels.Services;

namespace GrpCurl.Net.Studio.ViewModels.Documents;

/// <summary>
///     The History tab (FR-120..129): a searchable/filterable grid over the recorded invocations, with
///     pin, clear, NDJSON export, and replay-to-tab. Entries are already redacted on disk; this view only
///     reads + manages them. When capture is disabled (FR-129) a banner is shown.
/// </summary>
public sealed partial class HistoryDocumentViewModel : DocumentViewModel
{
    private const string All = "All";

    private readonly IHistoryStore _history;
    private readonly ISettingsStore _settings;
    private readonly IWorkspaceStore _workspace;
    private readonly IDocumentHost _host;
    private readonly IDialogService _dialogs;
    private readonly IFilePickerService? _filePicker;
    private readonly IUiDispatcher _dispatcher;

    private IReadOnlyList<HistoryEntry> _all = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    private bool _captureEnabled = true;

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private string _connectionFilter = All;

    [ObservableProperty]
    private string _categoryFilter = All;

    [ObservableProperty]
    private string _kindFilter = All;

    [ObservableProperty]
    private bool _pinnedOnly;

    public HistoryDocumentViewModel(
        IHistoryStore history,
        ISettingsStore settings,
        IWorkspaceStore workspace,
        IDocumentHost host,
        IDialogService dialogs,
        IUiDispatcher dispatcher,
        IFilePickerService? filePicker = null)
    {
        _history = history;
        _settings = settings;
        _workspace = workspace;
        _host = host;
        _dialogs = dialogs;
        _filePicker = filePicker;
        _dispatcher = dispatcher;

        Title = "History";
        ConnectionOptions = [All];
        _ = LoadAsync();
    }

    public ObservableCollection<HistoryRowViewModel> Rows { get; } = [];

    public ObservableCollection<string> ConnectionOptions { get; }

    public IReadOnlyList<string> CategoryOptions { get; } =
        [All, "success", "rpc-error", "transport", "cancelled", "input", "internal"];

    public IReadOnlyList<string> KindOptions { get; } = [All, "gRPC", "GraphQL"];

    public bool IsEmpty => Rows.Count == 0;

    public async Task LoadAsync()
    {
        CaptureEnabled = _settings.Current.History.Enabled;
        var entries = await _history.ReadAllAsync().ConfigureAwait(false);

        await _dispatcher.InvokeAsync(() =>
        {
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
        => !entry.Request.BodyTruncated && ResolveConnection(entry) is not null;

    private SavedConnection? ResolveConnection(HistoryEntry entry)
        => _workspace.Current.Connections.FirstOrDefault(c => c.Name == entry.Connection.Name);

    private static string KindLabel(HistoryKind kind) => kind == HistoryKind.Grpc ? "gRPC" : "GraphQL";

    [RelayCommand]
    private Task Refresh() => LoadAsync();

    /// <summary>FR-124: pin/unpin (pinned entries survive retention).</summary>
    [RelayCommand]
    private async Task TogglePin(HistoryRowViewModel? row)
    {
        if (row is not null)
        {
            await _history.SetPinnedAsync(row.Id, !row.Pinned);
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

        if (row.Entry.Request.BodyTruncated)
        {
            await _dialogs.ShowMessageAsync("Replay unavailable", "This entry's body was truncated, so it cannot be replayed.");
            return;
        }

        if (ResolveConnection(row.Entry) is not { } connection)
        {
            await _dialogs.ShowMessageAsync("Connection no longer exists",
                $"The connection '{row.Entry.Connection.Name}' is not in the current workspace, so this entry cannot be replayed.");
            return;
        }

        // FR-123: restore body + headers + options. A redacted secret value can't be recovered, so its
        // header is restored by name and flagged "value required"; ${VAR} headers come back verbatim and
        // re-resolve at send time. The replay is a plain draft (not bound to a saved request).
        _host.OpenInvocation(connection, row.Entry.Method, BuildPrefill(row.Entry.Request));
    }

    private static RequestPrefill BuildPrefill(HistoryRequest request)
    {
        var headers = request.Headers
            .Select(h => h.Value == HistoryEntry.RedactedMarker
                ? new PrefillHeader(h.Name, string.Empty, IsBin(h.Name), RequiresValue: true)
                : new PrefillHeader(h.Name, h.Value, IsBin(h.Name)))
            .ToList();

        return new RequestPrefill(
            request.Body,
            request.BodyFormat == "text" ? RequestBodyFormat.Text : RequestBodyFormat.Json,
            headers,
            request.Deadline,
            request.EmitDefaults,
            request.AllowUnknownFields,
            (request.MaxReceiveBytes ?? request.MaxSendBytes)?.ToString(CultureInfo.InvariantCulture));
    }

    private static bool IsBin(string name) => name.EndsWith("-bin", StringComparison.OrdinalIgnoreCase);

    [RelayCommand]
    private async Task DeleteSelected()
    {
        var ids = Rows.Where(r => r.IsSelected).Select(r => r.Id).ToList();

        if (ids.Count > 0)
        {
            await _history.DeleteAsync(ids);
            await LoadAsync();
        }
    }

    [RelayCommand]
    private async Task ClearUnpinned()
    {
        if (await _dialogs.ConfirmAsync("Clear unpinned history?", "Delete every unpinned entry? This cannot be undone."))
        {
            await _history.ClearAsync(keepPinned: true);
            await LoadAsync();
        }
    }

    [RelayCommand]
    private async Task ClearAll()
    {
        if (await _dialogs.ConfirmAsync("Clear all history?", "Delete every entry, including pinned ones? This cannot be undone."))
        {
            await _history.ClearAsync(keepPinned: false);
            await LoadAsync();
        }
    }

    /// <summary>FR-128: export the currently-filtered entries (already redacted) to an NDJSON file.</summary>
    [RelayCommand]
    private async Task Export()
    {
        if (_filePicker is null)
        {
            return;
        }

        var path = await _filePicker.SaveFileAsync("Export history", "history.ndjson", ["ndjson", "json"]);

        if (path is not null)
        {
            await _history.ExportAsync(path, Rows.Select(r => r.Entry).ToList());
        }
    }
}
