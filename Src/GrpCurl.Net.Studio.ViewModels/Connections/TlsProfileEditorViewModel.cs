using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GrpCurl.Net.Studio.ViewModels.Models.Connections;
using GrpCurl.Net.Studio.ViewModels.Services;

namespace GrpCurl.Net.Studio.ViewModels.Connections;

/// <summary>
///     Creates or edits a workspace TLS profile (FR-030..036). Hosted in a modal dialog; closes with the
///     saved <see cref="TlsProfile" /> or <see langword="null" /> on cancel. Certificate/key files are
///     referenced by path only (SEC-016); the PKCS12 password is the one secret, written to
///     <see cref="ISecretStore" /> on save and referenced from the profile (SEC-017). Studio adds no TLS
///     logic — Core remains the validation authority; the format hint here mirrors Core's content-based
///     PKCS12-vs-PEM detection only to guide the editor.
/// </summary>
public sealed partial class TlsProfileEditorViewModel : DialogViewModel<TlsProfile>
{
    private static readonly string[] RevocationModes = ["online", "offline", "nocheck"];

    private readonly IFilePickerService _filePicker;
    private readonly IDialogService _dialogService;
    private readonly ISecretStore _secretStore;
    private readonly string _id;
    private readonly string? _existingPasswordSecretRef;

    private bool _insecureConfirmed;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NameError))]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private string _name = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsCustomCa))]
    [NotifyPropertyChangedFor(nameof(IsSkipVerification))]
    [NotifyPropertyChangedFor(nameof(CaCertError))]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private TlsValidationMode _selectedValidationMode = TlsValidationMode.SystemRoots;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CaCertError))]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private string _caCertPath = string.Empty;

    [ObservableProperty]
    private string _selectedRevocationMode = "online";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ClientCertError))]
    [NotifyPropertyChangedFor(nameof(IsPkcs12))]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private string _clientCertPath = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ClientCertError))]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private string _clientKeyPath = string.Empty;

    /// <summary>"PEM", "PKCS12", or null when no client certificate is selected / unreadable.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPkcs12))]
    [NotifyPropertyChangedFor(nameof(ClientCertError))]
    [NotifyPropertyChangedFor(nameof(DetectedFormatDisplay))]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private string? _detectedClientCertFormat;

    [ObservableProperty]
    private string _clientCertPassword = string.Empty;

    [ObservableProperty]
    private bool _isPasswordRevealed;

    [ObservableProperty]
    private bool _exportableClientKey;

    /// <summary>FR-037: parsed facts for the selected CA / client certificate (null when not inspectable).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasCaCertFacts))]
    private CertificateFacts? _caCertFacts;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasClientCertFacts))]
    private CertificateFacts? _clientCertFacts;

    public TlsProfileEditorViewModel(
        IFilePickerService filePicker,
        IDialogService dialogService,
        ISecretStore secretStore,
        TlsProfile? existing = null)
    {
        _filePicker = filePicker;
        _dialogService = dialogService;
        _secretStore = secretStore;
        IsEdit = existing is not null;

        var p = existing ?? new TlsProfile();
        _id = p.Id;
        _existingPasswordSecretRef = p.ClientCertPasswordSecretRef;
        _name = p.Name;

        _selectedValidationMode = p.InsecureSkipVerify
            ? TlsValidationMode.SkipVerification
            : !string.IsNullOrWhiteSpace(p.CaCertPath) ? TlsValidationMode.CustomCa : TlsValidationMode.SystemRoots;

        // Editing an already-insecure profile shouldn't re-prompt on first render.
        _insecureConfirmed = p.InsecureSkipVerify;

        _caCertPath = p.CaCertPath ?? string.Empty;
        _selectedRevocationMode = string.IsNullOrWhiteSpace(p.RevocationMode) ? "online" : p.RevocationMode;
        _clientCertPath = p.ClientCertPath ?? string.Empty;
        _clientKeyPath = p.ClientKeyPath ?? string.Empty;
        _exportableClientKey = p.ExportableClientKey;
        _detectedClientCertFormat = DetectFormat(_clientCertPath);
        _caCertFacts = CertificateInspector.TryRead(_caCertPath);
        _clientCertFacts = CertificateInspector.TryRead(_clientCertPath);
    }

    public bool IsEdit { get; }

    /// <summary>FR-037: whether parsed CA / client certificate facts are available to display.</summary>
    public bool HasCaCertFacts => CaCertFacts is not null;

    public bool HasClientCertFacts => ClientCertFacts is not null;

    public string Title => IsEdit ? "Edit TLS profile" : "New TLS profile";

    public IReadOnlyList<TlsValidationMode> ValidationModes { get; } =
        [TlsValidationMode.SystemRoots, TlsValidationMode.CustomCa, TlsValidationMode.SkipVerification];

    public IReadOnlyList<string> RevocationModeOptions => RevocationModes;

    public bool IsCustomCa => SelectedValidationMode == TlsValidationMode.CustomCa;

    public bool IsSkipVerification => SelectedValidationMode == TlsValidationMode.SkipVerification;

    /// <summary>True once a PKCS12 client bundle is detected — enables the password + exportable fields.</summary>
    public bool IsPkcs12 => DetectedClientCertFormat == "PKCS12";

    public string? DetectedFormatDisplay => DetectedClientCertFormat is { } f ? $"Detected: {f}" : null;

    public string? NameError => string.IsNullOrWhiteSpace(Name) ? "Name is required." : null;

    public string? CaCertError =>
        IsCustomCa && string.IsNullOrWhiteSpace(CaCertPath) ? "Custom CA requires a certificate file." : null;

    /// <summary>FR-035: at edit time, reject a half-specified client certificate (the CLI fails at call time).</summary>
    public string? ClientCertError
    {
        get
        {
            var hasCert = !string.IsNullOrWhiteSpace(ClientCertPath);
            var hasKey = !string.IsNullOrWhiteSpace(ClientKeyPath);

            if (hasKey && !hasCert)
            {
                return "A client key requires a client certificate.";
            }

            // A PEM certificate needs its key; a PKCS12 bundle carries the key itself.
            if (hasCert && !hasKey && DetectedClientCertFormat == "PEM")
            {
                return "A PEM client certificate requires a key file (or supply a PKCS12 bundle).";
            }

            return null;
        }
    }

    private bool CanSave => NameError is null && CaCertError is null && ClientCertError is null;

    [RelayCommand]
    private async Task BrowseCaCert()
    {
        var path = await _filePicker.OpenFileAsync("Select CA certificate", ["pem", "crt", "cer"]);

        if (path is not null)
        {
            CaCertPath = path;
        }
    }

    // FR-037: re-parse the certificate facts whenever the path changes (browse or manual edit).
    partial void OnCaCertPathChanged(string value) => CaCertFacts = CertificateInspector.TryRead(value);

    partial void OnClientCertPathChanged(string value) => ClientCertFacts = CertificateInspector.TryRead(value);

    [RelayCommand]
    private async Task BrowseClientCert()
    {
        var path = await _filePicker.OpenFileAsync("Select client certificate", ["pem", "crt", "cer", "p12", "pfx"]);

        if (path is not null)
        {
            ClientCertPath = path;
            DetectedClientCertFormat = DetectFormat(path);
        }
    }

    [RelayCommand]
    private async Task BrowseClientKey()
    {
        var path = await _filePicker.OpenFileAsync("Select client private key", ["pem", "key"]);

        if (path is not null)
        {
            ClientKeyPath = path;
        }
    }

    [RelayCommand(CanExecute = nameof(CanSave))]
    private async Task Save()
    {
        var passwordRef = await ResolvePasswordRefAsync();

        var profile = new TlsProfile
        {
            Id = _id,
            Name = Name.Trim(),
            InsecureSkipVerify = SelectedValidationMode == TlsValidationMode.SkipVerification,
            CaCertPath = IsCustomCa ? NullIfBlank(CaCertPath) : null,
            RevocationMode = IsCustomCa ? SelectedRevocationMode : null,
            ClientCertPath = NullIfBlank(ClientCertPath),
            // PKCS12 carries its own key; only a PEM pair has a separate key file.
            ClientKeyPath = IsPkcs12 ? null : NullIfBlank(ClientKeyPath),
            ClientCertPasswordSecretRef = passwordRef,
            ExportableClientKey = IsPkcs12 && ExportableClientKey
        };

        Close(profile);
    }

    [RelayCommand]
    private void Cancel() => Close(null);

    /// <summary>
    ///     Stores the PKCS12 password and returns its secret reference, reusing the existing reference so a
    ///     re-save overwrites the same entry. A blank password keeps an existing reference (the user left it
    ///     untouched) or clears it when there was none.
    /// </summary>
    private async Task<string?> ResolvePasswordRefAsync()
    {
        if (!IsPkcs12 || string.IsNullOrEmpty(ClientCertPassword))
        {
            return IsPkcs12 ? _existingPasswordSecretRef : null;
        }

        var keyRef = _existingPasswordSecretRef ?? Guid.NewGuid().ToString("N");
        await _secretStore.SetAsync(keyRef, ClientCertPassword);
        return keyRef;
    }

    // FR-031: selecting Skip verification demands an explicit confirmation; declining reverts the choice.
    partial void OnSelectedValidationModeChanged(TlsValidationMode oldValue, TlsValidationMode newValue)
    {
        if (newValue == TlsValidationMode.SkipVerification && !_insecureConfirmed)
        {
            _ = ConfirmInsecureAsync(oldValue);
        }
    }

    private async Task ConfirmInsecureAsync(TlsValidationMode revertTo)
    {
        var confirmed = await _dialogService.ConfirmAsync(
            "Disable TLS verification?",
            "Skip verification turns off all certificate checks for connections using this profile. "
            + "Use it only for test or diagnostic targets. Continue?");

        if (confirmed)
        {
            _insecureConfirmed = true;
        }
        else
        {
            SelectedValidationMode = revertTo;
        }
    }

    /// <summary>
    ///     Content-based format hint mirroring Core's detection: a PEM file is ASCII beginning with a
    ///     <c>-----BEGIN</c> armor line; anything else readable is treated as a PKCS12/DER bundle. Returns
    ///     null when there is no path or the file can't be read (Core stays the authority at call time).
    /// </summary>
    private static string? DetectFormat(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return null;
        }

        try
        {
            using var stream = File.OpenRead(path);
            Span<byte> head = stackalloc byte[16];
            var read = stream.Read(head);
            var text = System.Text.Encoding.ASCII.GetString(head[..read]);
            return text.Contains("-----BEGIN", StringComparison.Ordinal) ? "PEM" : "PKCS12";
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static string? NullIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;
}
