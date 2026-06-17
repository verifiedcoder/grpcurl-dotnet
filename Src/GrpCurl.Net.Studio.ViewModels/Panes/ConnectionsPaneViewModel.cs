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
        IDocumentHost? documentHost = null)
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
                item.SavedRequests.Add(new SavedRequestItemViewModel(request, OpenSavedRequestAsync, _savedRequests, _dialogService));
            }
        }
    }

    /// <summary>
    ///     Builds a connection list item and populates its saved requests (FR-145) from the workspace, each
    ///     wired to open into a pre-filled invocation tab.
    /// </summary>
    private ConnectionListItemViewModel CreateItem(SavedConnection connection)
    {
        var item = new ConnectionListItemViewModel(connection);

        foreach (var request in _savedRequests?.ForConnection(connection.Id) ?? [])
        {
            item.SavedRequests.Add(new SavedRequestItemViewModel(request, OpenSavedRequestAsync, _savedRequests, _dialogService));
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
            _registry, existing: null, _settings?.Current.Network, _profileStore, _filePicker, _dialogService, _secretStore, _protocService);
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
            _registry, item.Connection, networkDefaults: null, _profileStore, _filePicker, _dialogService, _secretStore, _protocService);
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

        var confirmed = await _dialogService.ConfirmAsync(
            "Delete connection",
            $"Delete '{item.Connection.Name}'? This cannot be undone.");

        if (confirmed)
        {
            Connections.Remove(item);
            await PersistAsync();
        }
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
