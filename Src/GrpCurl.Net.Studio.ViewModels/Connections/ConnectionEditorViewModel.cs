using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GrpCurl.Net.Studio.ViewModels.Models.Connections;
using GrpCurl.Net.Studio.ViewModels.Services;

namespace GrpCurl.Net.Studio.ViewModels.Connections;

/// <summary>
///     Edits a connection (FR-010..019). Hosted in a modal dialog; closes with the saved
///     <see cref="SavedConnection" /> or <see langword="null" /> on cancel. Validation mirrors
///     the CLI grammar (<see cref="ConnectionValidation" />); the test-connection probe runs
///     through the real <see cref="IConnectionRegistry" />.
/// </summary>
public sealed partial class ConnectionEditorViewModel : DialogViewModel<SavedConnection>
{
    private readonly IConnectionRegistry _registry;
    private readonly string _id;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NameError))]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private string _name = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AddressError))]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    [NotifyCanExecuteChangedFor(nameof(TestConnectionCommand))]
    private string _address = string.Empty;

    /// <summary>True = plaintext; false = TLS (default).</summary>
    [ObservableProperty]
    private bool _isPlaintext;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ConnectTimeoutError))]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private string _connectTimeout = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(KeepaliveTimeError))]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private string _keepaliveTime = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(KeepaliveTimeoutError))]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private string _keepaliveTimeout = string.Empty;

    [ObservableProperty]
    private string _authority = string.Empty;

    [ObservableProperty]
    private string _serverName = string.Empty;

    [ObservableProperty]
    private string _userAgent = string.Empty;

    [ObservableProperty]
    private string _notes = string.Empty;

    [ObservableProperty]
    private TestConnectionResult? _lastTestResult;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsTesting))]
    private bool _isTestRunning;

    public ConnectionEditorViewModel(IConnectionRegistry registry, SavedConnection? existing = null)
    {
        _registry = registry;
        IsEdit = existing is not null;

        var c = existing ?? new SavedConnection();
        _id = c.Id;
        _name = c.Name;
        _address = c.Address;
        _isPlaintext = c.Transport == TransportMode.Plaintext;
        _connectTimeout = c.ConnectTimeout ?? string.Empty;
        _keepaliveTime = c.Keepalive.Time ?? string.Empty;
        _keepaliveTimeout = c.Keepalive.Timeout ?? string.Empty;
        _authority = c.Authority ?? string.Empty;
        _serverName = c.ServerName ?? string.Empty;
        _userAgent = c.UserAgent ?? string.Empty;
        _notes = c.Notes ?? string.Empty;

        ReflectionHeaders = new ObservableCollection<HeaderRowViewModel>(
            c.ReflectionHeaders.Select(h => new HeaderRowViewModel(h)));
    }

    public bool IsEdit { get; }

    public string Title => IsEdit ? "Edit connection" : "New connection";

    public ObservableCollection<HeaderRowViewModel> ReflectionHeaders { get; }

    public bool IsTesting => IsTestRunning;

    public string? NameError => string.IsNullOrWhiteSpace(Name) ? "Name is required." : null;

    public string? AddressError => ConnectionValidation.ValidateAddress(Address);

    public string? ConnectTimeoutError => ConnectionValidation.ValidateDuration(ConnectTimeout);

    public string? KeepaliveTimeError => ConnectionValidation.ValidateDuration(KeepaliveTime);

    public string? KeepaliveTimeoutError => ConnectionValidation.ValidateDuration(KeepaliveTimeout);

    private bool CanSave
        => NameError is null && AddressError is null && ConnectTimeoutError is null
           && KeepaliveTimeError is null && KeepaliveTimeoutError is null;

    private bool CanTest => AddressError is null && !string.IsNullOrWhiteSpace(Address);

    [RelayCommand]
    private void AddHeader() => ReflectionHeaders.Add(new HeaderRowViewModel());

    [RelayCommand]
    private void RemoveHeader(HeaderRowViewModel? row)
    {
        if (row is not null)
        {
            ReflectionHeaders.Remove(row);
        }
    }

    [RelayCommand(CanExecute = nameof(CanTest), IncludeCancelCommand = true)]
    private async Task TestConnection(CancellationToken cancellationToken)
    {
        IsTestRunning = true;
        LastTestResult = null;

        try
        {
            LastTestResult = await _registry.TestConnectionAsync(BuildConnection(), cancellationToken);
        }
        catch (OperationCanceledException)
        {
            LastTestResult = TestConnectionResult.Failure("Test cancelled.");
        }
        finally
        {
            IsTestRunning = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanSave))]
    private void Save() => Close(BuildConnection());

    [RelayCommand]
    private void Cancel() => Close(null);

    public SavedConnection BuildConnection() => new()
    {
        Id = _id,
        Name = Name.Trim(),
        Address = Address.Trim(),
        Transport = IsPlaintext ? TransportMode.Plaintext : TransportMode.Tls,
        ConnectTimeout = NullIfBlank(ConnectTimeout),
        Keepalive = new KeepaliveSettings { Time = NullIfBlank(KeepaliveTime), Timeout = NullIfBlank(KeepaliveTimeout) },
        Authority = NullIfBlank(Authority),
        ServerName = NullIfBlank(ServerName),
        UserAgent = NullIfBlank(UserAgent),
        ReflectionHeaders = ReflectionHeaders
            .Where(h => !string.IsNullOrWhiteSpace(h.Name))
            .Select(h => h.ToEntry())
            .ToList(),
        DescriptorMode = DescriptorMode.Reflection,
        Notes = NullIfBlank(Notes)
    };

    private static string? NullIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;
}
