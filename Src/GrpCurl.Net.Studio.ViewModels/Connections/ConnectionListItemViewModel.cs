using System.Collections.ObjectModel;
using System.Collections.Specialized;
using CommunityToolkit.Mvvm.ComponentModel;
using GrpCurl.Net.Studio.ViewModels.Models.Connections;

namespace GrpCurl.Net.Studio.ViewModels.Connections;

/// <summary>A connection as shown in the sidebar list: name, address, and live status dot (FR-019).</summary>
public sealed partial class ConnectionListItemViewModel : ViewModelBase
{
    [ObservableProperty]
    private ConnectionStatus _status = ConnectionStatus.Unknown;

    [ObservableProperty]
    private string? _statusDetail;

    public ConnectionListItemViewModel(SavedConnection connection)
    {
        Connection = connection;
        SavedRequests.CollectionChanged += OnSavedRequestsChanged;
    }

    public SavedConnection Connection { get; }

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
