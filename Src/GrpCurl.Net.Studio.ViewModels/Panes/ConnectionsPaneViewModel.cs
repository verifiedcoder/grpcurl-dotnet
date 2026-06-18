using System.Collections.ObjectModel;
using System.Collections.Specialized;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GrpCurl.Net.Studio.ViewModels.Connections;
using GrpCurl.Net.Studio.ViewModels.Models.Connections;
using GrpCurl.Net.Studio.ViewModels.Services;

namespace GrpCurl.Net.Studio.ViewModels.Panes;

/// <summary>
///     Top section of the left sidebar: saved connections (FR-010). Lists connections with live
///     status, supports add/edit/duplicate/delete, and persists changes through the workspace
///     store. The connection editor is shown via the dialog service so this view model stays
///     headless-testable.
/// </summary>
public sealed partial class ConnectionsPaneViewModel : ViewModelBase
{
    private readonly IWorkspaceStore _workspaceStore;
    private readonly IConnectionRegistry _registry;
    private readonly IDialogService _dialogService;
    private readonly IConnectionSelection _selection;
    private readonly ISettingsStore? _settings;
    private readonly ITlsProfileStore? _profileStore;
    private readonly IFilePickerService? _filePicker;
    private readonly ISecretStore? _secretStore;
    private readonly IProtocService? _protocService;
    private readonly ISavedRequestStore? _savedRequests;
    private readonly IDocumentHost? _documentHost;
    private readonly ISavedRequestSnippetIO? _snippetIO;
    private readonly ConsoleViewModel? _console;
    private readonly IHistoryStore? _history;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(EditConnectionCommand))]
    [NotifyCanExecuteChangedFor(nameof(DuplicateConnectionCommand))]
    [NotifyCanExecuteChangedFor(nameof(DeleteConnectionCommand))]
    private ConnectionListItemViewModel? _selectedConnection;

    public ConnectionsPaneViewModel(
        IWorkspaceStore workspaceStore,
        IConnectionRegistry registry,
        IDialogService dialogService,
        IConnectionSelection selection,
        ISettingsStore? settings = null,
        ITlsProfileStore? profileStore = null,
        IFilePickerService? filePicker = null,
        ISecretStore? secretStore = null,
        IProtocService? protocService = null,
        ISavedRequestStore? savedRequests = null,
        IDocumentHost? documentHost = null,
        ISavedRequestSnippetIO? snippetIO = null,
        ConsoleViewModel? console = null,
        IHistoryStore? history = null)
    {
        _workspaceStore = workspaceStore;
        _registry = registry;
        _dialogService = dialogService;
        _selection = selection;
        _settings = settings;
        _profileStore = profileStore;
        _filePicker = filePicker;
        _secretStore = secretStore;
        _protocService = protocService;
        _savedRequests = savedRequests;
        _documentHost = documentHost;
        _snippetIO = snippetIO;
        _console = console;
        _history = history;

        Connections = [];
        Connections.CollectionChanged += OnConnectionsChanged;

        foreach (var connection in workspaceStore.Current.Connections)
        {
            Connections.Add(CreateItem(connection));
        }

        // FR-145: keep the nested saved-request lists in sync as requests are saved/deleted.
        if (_savedRequests is not null)
        {
            _savedRequests.Changed += (_, _) => RefreshSavedRequests();
        }
    }

    /// <summary>Re-populates every connection's nested saved-request list from the store (FR-145).</summary>
    private void RefreshSavedRequests()
    {
        foreach (var item in Connections)
        {
            item.SavedRequests.Clear();

            foreach (var request in _savedRequests?.ForConnection(item.Connection.Id) ?? [])
            {
                item.SavedRequests.Add(new SavedRequestItemViewModel(request, OpenSavedRequestAsync, _savedRequests, _dialogService, _filePicker, _snippetIO, _console));
            }
        }
    }

    /// <summary>
    ///     Builds a connection list item and populates its saved requests (FR-145) from the workspace, each
    ///     wired to open into a pre-filled invocation tab.
    /// </summary>
    private ConnectionListItemViewModel CreateItem(SavedConnection connection)
    {
        var canImport = _filePicker is not null && _snippetIO is not null && _savedRequests is not null;
        var item = new ConnectionListItemViewModel(connection, canImport ? ImportRequestIntoAsync : null);

        foreach (var request in _savedRequests?.ForConnection(connection.Id) ?? [])
        {
            item.SavedRequests.Add(new SavedRequestItemViewModel(request, OpenSavedRequestAsync, _savedRequests, _dialogService, _filePicker, _snippetIO, _console));
        }

        return item;
    }

    private Task OpenSavedRequestAsync(SavedRequest request)
    {
        var connection = Connections.FirstOrDefault(c => c.Id == request.ConnectionId)?.Connection;

        if (connection is not null)
        {
            _documentHost?.OpenSavedRequest(connection, request);
        }

        return Task.CompletedTask;
    }

    /// <summary>FR-166: import a saved-request snippet into a connection (re-bound, with a deduped name).</summary>
    private async Task ImportRequestIntoAsync(ConnectionListItemViewModel item)
    {
        if (_filePicker is null || _snippetIO is null || _savedRequests is null)
        {
            return;
        }

        var path = await _filePicker.OpenFileAsync("Import request", ["grpcnreq.json", "json"]);

        if (path is null)
        {
            return;
        }

        SavedRequest request;

        try
        {
            request = await _snippetIO.ImportAsync(path);
        }
        catch (SavedRequestSnippetException ex)
        {
            await _dialogService.ShowMessageAsync("Could not import request", ex.Message);
            return;
        }

        // Re-bind the imported request to the target connection with a fresh id + a non-colliding name.
        request.Id = Guid.NewGuid().ToString();
        request.ConnectionId = item.Connection.Id;
        request.Name = DedupRequestName(request.Name, item.Connection.Id);

        await _savedRequests.SaveAsync(request);
    }

    private string DedupRequestName(string name, string connectionId)
    {
        var taken = new HashSet<string>(
            _savedRequests?.ForConnection(connectionId).Select(r => r.Name) ?? [], StringComparer.Ordinal);

        var candidate = taken.Contains(name) ? $"{name} (imported)" : name;

        for (var n = 2; taken.Contains(candidate); n++)
        {
            candidate = $"{name} (imported {n})";
        }

        return candidate;
    }

    public string Header => "Connections";

    /// <summary>
    ///     E3.1: rebuilds the connection list from the active workspace after it changes (open / new /
    ///     reload), clearing the current selection so a stale connection isn't left selected.
    /// </summary>
    public void ReloadFromWorkspace()
    {
        _selection.Set(null);
        SelectedConnection = null;
        Connections.Clear();

        foreach (var connection in _workspaceStore.Current.Connections)
        {
            Connections.Add(CreateItem(connection));
        }
    }

    public ObservableCollection<ConnectionListItemViewModel> Connections { get; }

    public bool HasConnections => Connections.Count > 0;

    /// <summary>Edit/duplicate/delete act on the selected connection, so they require one.</summary>
    private bool HasSelection => SelectedConnection is not null;

    /// <summary>Publishes the active connection so the explorer (and later, invocation tabs) can react.</summary>
    partial void OnSelectedConnectionChanged(ConnectionListItemViewModel? value)
        => _selection.Set(value?.Connection);

    [RelayCommand]
    private async Task AddConnection()
    {
        var editor = new ConnectionEditorViewModel(
            _registry, existing: null, _settings?.Current.Network, _profileStore, _filePicker, _dialogService, _secretStore, _protocService, _console);
        var saved = await _dialogService.ShowDialogAsync(editor);

        if (saved is not null)
        {
            Connections.Add(CreateItem(saved));
            await PersistAsync();
        }
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private async Task EditConnection(ConnectionListItemViewModel? item)
    {
        item ??= SelectedConnection;

        if (item is null)
        {
            return;
        }

        var editor = new ConnectionEditorViewModel(
            _registry, item.Connection, networkDefaults: null, _profileStore, _filePicker, _dialogService, _secretStore, _protocService, _console);
        var saved = await _dialogService.ShowDialogAsync(editor);

        if (saved is not null)
        {
            var index = Connections.IndexOf(item);
            Connections[index] = CreateItem(saved);
            await PersistAsync();
        }
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private async Task DuplicateConnection(ConnectionListItemViewModel? item)
    {
        item ??= SelectedConnection;

        if (item is null)
        {
            return;
        }

        var copy = item.Connection.Clone();
        copy.Name = $"{item.Connection.Name} (copy)";

        Connections.Add(CreateItem(copy));
        await PersistAsync();
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private async Task DeleteConnection(ConnectionListItemViewModel? item)
    {
        item ??= SelectedConnection;

        if (item is null)
        {
            return;
        }

        // FR-126: find this connection's history (by snapshot name + address) so the dialog can offer to purge it.
        var historyIds = await MatchingHistoryIdsAsync(item.Connection);
        bool purgeHistory;

        if (historyIds.Count > 0)
        {
            var choice = await _dialogService.ShowDialogAsync(
                new DeleteConnectionDialogViewModel(item.Connection.Name, historyIds.Count));

            if (choice is null)
            {
                return; // cancelled
            }

            purgeHistory = choice.Value;
        }
        else if (!await _dialogService.ConfirmAsync(
                     "Delete connection", $"Delete '{item.Connection.Name}'? This cannot be undone."))
        {
            return;
        }
        else
        {
            purgeHistory = false;
        }

        Connections.Remove(item);
        await PersistAsync();

        if (purgeHistory && _history is not null)
        {
            await _history.DeleteAsync(historyIds);
        }
    }

    // FR-126: history entries whose snapshot connection (name + address) matches the one being deleted.
    private async Task<IReadOnlyList<string>> MatchingHistoryIdsAsync(SavedConnection connection)
    {
        if (_history is null)
        {
            return [];
        }

        var entries = await _history.ReadAllAsync();
        return entries
            .Where(e => e.Connection.Name == connection.Name && e.Connection.Address == connection.Address)
            .Select(e => e.Id)
            .ToList();
    }

    /// <summary>Opens the editor for a connection by identity (the insecure banner's "Review connection…").</summary>
    public async Task ReviewConnectionAsync(SavedConnection connection)
    {
        var item = Connections.FirstOrDefault(i => i.Connection.Id == connection.Id);

        if (item is not null)
        {
            SelectedConnection = item;
            await EditConnectionCommand.ExecuteAsync(item);
        }
    }

    private void OnConnectionsChanged(object? sender, NotifyCollectionChangedEventArgs e)
        => OnPropertyChanged(nameof(HasConnections));

    private Task PersistAsync()
    {
        // Clone the live workspace so the connection-list save preserves everything else — the workspace
        // id/name (which namespaces secrets), TLS profiles, and any forward-compat fields.
        var workspace = _workspaceStore.Current.Copy();
        workspace.Connections = Connections.Select(i => i.Connection).ToList();

        return _workspaceStore.SaveAsync(workspace);
    }
}
