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
        ISecretStore? secretStore = null)
    {
        _workspaceStore = workspaceStore;
        _registry = registry;
        _dialogService = dialogService;
        _selection = selection;
        _settings = settings;
        _profileStore = profileStore;
        _filePicker = filePicker;
        _secretStore = secretStore;

        Connections = [];
        Connections.CollectionChanged += OnConnectionsChanged;

        foreach (var connection in workspaceStore.Current.Connections)
        {
            Connections.Add(new ConnectionListItemViewModel(connection));
        }
    }

    public string Header => "Connections";

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
            _registry, existing: null, _settings?.Current.Network, _profileStore, _filePicker, _dialogService, _secretStore);
        var saved = await _dialogService.ShowDialogAsync(editor);

        if (saved is not null)
        {
            Connections.Add(new ConnectionListItemViewModel(saved));
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
            _registry, item.Connection, networkDefaults: null, _profileStore, _filePicker, _dialogService, _secretStore);
        var saved = await _dialogService.ShowDialogAsync(editor);

        if (saved is not null)
        {
            var index = Connections.IndexOf(item);
            Connections[index] = new ConnectionListItemViewModel(saved);
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

        Connections.Add(new ConnectionListItemViewModel(copy));
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

    private void OnConnectionsChanged(object? sender, NotifyCollectionChangedEventArgs e)
        => OnPropertyChanged(nameof(HasConnections));

    private Task PersistAsync()
    {
        // Carry the rest of the workspace forward — TLS profiles are workspace-level (E2.2) and must
        // survive a connection-list save, just as a profile save must preserve the connection list.
        var current = _workspaceStore.Current;

        var workspace = new WorkspaceModel
        {
            SchemaVersion = current.SchemaVersion,
            Connections = Connections.Select(i => i.Connection).ToList(),
            TlsProfiles = [.. current.TlsProfiles]
        };

        return _workspaceStore.SaveAsync(workspace);
    }
}
