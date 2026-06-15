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

    public ConnectionListItemViewModel(SavedConnection connection) => Connection = connection;

    public SavedConnection Connection { get; }

    public string Id => Connection.Id;

    public string Name => Connection.Name;

    public string Address => Connection.Address;

    public string TransportLabel => Connection.Transport == TransportMode.Plaintext ? "plaintext" : "TLS";
}
