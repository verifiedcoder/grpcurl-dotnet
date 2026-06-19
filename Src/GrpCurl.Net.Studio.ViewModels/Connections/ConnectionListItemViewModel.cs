using System.Collections.ObjectModel;
using System.Collections.Specialized;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GrpCurl.Net.Studio.ViewModels.Models.Connections;

namespace GrpCurl.Net.Studio.ViewModels.Connections;

/// <summary>A connection as shown in the sidebar list: name, address, and live status dot (FR-019).</summary>
public sealed partial class ConnectionListItemViewModel : ViewModelBase
{
    private readonly Func<ConnectionListItemViewModel, Task>? _importRequest;

    [ObservableProperty]
    public partial ConnectionStatus Status { get; set; } = ConnectionStatus.Unknown;

    [ObservableProperty]
    public partial string? StatusDetail { get; set; }

    public ConnectionListItemViewModel(
        SavedConnection connection, Func<ConnectionListItemViewModel, Task>? importRequest = null)
    {
        Connection = connection;
        _importRequest = importRequest;
        SavedRequests.CollectionChanged += OnSavedRequestsChanged;
    }

    public SavedConnection Connection { get; }

    /// <summary>FR-166: whether a saved-request snippet can be imported into this connection.</summary>
    public bool CanImportRequest => _importRequest is not null;

    /// <summary>FR-166: import a saved-request snippet into this connection.</summary>
    [RelayCommand(CanExecute = nameof(CanImportRequest))]
    private Task ImportRequest() => _importRequest?.Invoke(this) ?? Task.CompletedTask;

    /// <summary>The connection's saved requests, shown nested beneath it in the sidebar (FR-145).</summary>
    public ObservableCollection<SavedRequestItemViewModel> SavedRequests { get; } = [];

    /// <summary>True when this connection has at least one saved request (drives the nested list's visibility).</summary>
    public bool HasSavedRequests => SavedRequests.Count > 0;

    public string Id => Connection.Id;

    public string Name => Connection.Name;

    public string Address => Connection.Address;

    public string TransportLabel => Connection.Transport == TransportMode.Plaintext ? "plaintext" : "TLS";

    private void OnSavedRequestsChanged(object? sender, NotifyCollectionChangedEventArgs e)
        => OnPropertyChanged(nameof(HasSavedRequests));
}
