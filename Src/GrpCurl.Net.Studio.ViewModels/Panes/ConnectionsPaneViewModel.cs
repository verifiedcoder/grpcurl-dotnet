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

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(EditConnectionCommand))]
    [NotifyCanExecuteChangedFor(nameof(DuplicateConnectionCommand))]
    [NotifyCanExecuteChangedFor(nameof(DeleteConnectionCommand))]
    private ConnectionListItemViewModel? _selectedConnection;

    public ConnectionsPaneViewModel(IWorkspaceStore workspaceStore, IConnectionRegistry registry, IDialogService dialogService)
    {
        _workspaceStore = workspaceStore;
        _registry = registry;
        _dialogService = dialogService;

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

    [RelayCommand]
    private async Task AddConnection()
    {
        var editor = new ConnectionEditorViewModel(_registry);
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

        var editor = new ConnectionEditorViewModel(_registry, item.Connection);
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
        var workspace = new WorkspaceModel
        {
            Connections = Connections.Select(i => i.Connection).ToList()
        };

        return _workspaceStore.SaveAsync(workspace);
    }
}
