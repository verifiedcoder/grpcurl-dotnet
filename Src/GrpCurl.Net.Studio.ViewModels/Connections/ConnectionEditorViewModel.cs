using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GrpCurl.Net.DescriptorSources;
using GrpCurl.Net.Studio.ViewModels.Models;
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
    private readonly ITlsProfileStore? _profileStore;
    private readonly IFilePickerService? _filePicker;
    private readonly IDialogService? _dialogService;
    private readonly ISecretStore? _secretStore;
    private readonly IProtocService? _protocService;
    private readonly string _id;
    private readonly DescriptorSourceConfig _descriptorSource;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NameError))]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private string _name = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AddressError))]
    [NotifyPropertyChangedFor(nameof(IsTlsProfileEnabled))]
    [NotifyPropertyChangedFor(nameof(IsUnixSocket))]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    [NotifyCanExecuteChangedFor(nameof(TestConnectionCommand))]
    private string _address = string.Empty;

    /// <summary>True = plaintext; false = TLS (default).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsTlsProfileEnabled))]
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

    /// <summary>The picked TLS profile (or the system-default sentinel); only meaningful under TLS.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(EditProfileCommand))]
    private TlsProfileOption? _selectedTlsProfile;

    /// <summary>Reflection (default) / Protoset / Proto — the descriptor source (FR-040).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsReflectionMode))]
    [NotifyPropertyChangedFor(nameof(IsProtosetMode))]
    [NotifyPropertyChangedFor(nameof(IsProtoMode))]
    [NotifyPropertyChangedFor(nameof(EffectiveSourceText))]
    private DescriptorMode _selectedDescriptorMode;

    /// <summary>Set when Proto mode is selected but protoc can't be found (FR-044 remediation).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowProtocRemediation))]
    private bool _protocMissing;

    [ObservableProperty]
    private string? _protocStatus;

    // FR-049: per-connection descriptor-limit overrides (blank = use Core's default).
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LimitsError))]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private string _maxProtosetFileBytesOverride = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LimitsError))]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private string _maxReflectionDescriptorBytesOverride = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LimitsError))]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private string _maxFileDescriptorsOverride = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LimitsError))]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private string _maxDependencyDepthOverride = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LimitsError))]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private string _maxSymbolsOverride = string.Empty;

    public ConnectionEditorViewModel(
        IConnectionRegistry registry,
        SavedConnection? existing = null,
        NetworkSettings? networkDefaults = null,
        ITlsProfileStore? profileStore = null,
        IFilePickerService? filePicker = null,
        IDialogService? dialogService = null,
        ISecretStore? secretStore = null,
        IProtocService? protocService = null)
    {
        _registry = registry;
        _profileStore = profileStore;
        _filePicker = filePicker;
        _dialogService = dialogService;
        _secretStore = secretStore;
        _protocService = protocService;
        IsEdit = existing is not null;

        var c = existing ?? new SavedConnection();
        _id = c.Id;
        _name = c.Name;
        _address = c.Address;
        _isPlaintext = c.Transport == TransportMode.Plaintext;

        _descriptorSource = c.DescriptorSource.Clone();
        _selectedDescriptorMode = _descriptorSource.Mode;
        ProtosetRows = new ObservableCollection<DescriptorPathRow>(_descriptorSource.ProtosetPaths.Select(DescriptorPathRow.ForProtoset));
        ProtoFileRows = new ObservableCollection<DescriptorPathRow>(_descriptorSource.ProtoFiles.Select(DescriptorPathRow.ForProtoFile));
        ImportPathRows = new ObservableCollection<DescriptorPathRow>(_descriptorSource.ImportPaths.Select(DescriptorPathRow.ForImportPath));

        _maxProtosetFileBytesOverride = OverrideText(_descriptorSource.MaxProtosetFileBytes);
        _maxReflectionDescriptorBytesOverride = OverrideText(_descriptorSource.MaxReflectionDescriptorBytes);
        _maxFileDescriptorsOverride = OverrideText(_descriptorSource.MaxFileDescriptors);
        _maxDependencyDepthOverride = OverrideText(_descriptorSource.MaxDependencyDepth);
        _maxSymbolsOverride = OverrideText(_descriptorSource.MaxSymbols);

        if (_selectedDescriptorMode == DescriptorMode.Proto)
        {
            _ = DetectProtocAsync();
        }

        TlsProfiles = [];
        RebuildProfileOptions(c.TlsProfileId);

        // FR-153: a brand-new connection seeds its network fields from the app defaults; editing an
        // existing one keeps that connection's own values.
        _connectTimeout = c.ConnectTimeout ?? (existing is null ? networkDefaults?.ConnectTimeout : null) ?? string.Empty;
        _keepaliveTime = c.Keepalive.Time ?? (existing is null ? networkDefaults?.KeepaliveTime : null) ?? string.Empty;
        _keepaliveTimeout = c.Keepalive.Timeout ?? (existing is null ? networkDefaults?.KeepaliveTimeout : null) ?? string.Empty;
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

    /// <summary>System-default sentinel plus every workspace TLS profile (FR-012/FR-030).</summary>
    public ObservableCollection<TlsProfileOption> TlsProfiles { get; }

    /// <summary>TLS profiles apply only under TLS over TCP; the picker is disabled for plaintext and Unix sockets.</summary>
    public bool IsTlsProfileEnabled => !IsPlaintext && !ConnectionValidation.IsUnixSocket(Address);

    /// <summary>True when the address is a Unix socket — TLS doesn't apply (FR-011).</summary>
    public bool IsUnixSocket => ConnectionValidation.IsUnixSocket(Address);

    /// <summary>Create/edit are only offered when the profile services are wired (they are in the app; not in bare unit ctors).</summary>
    public bool CanManageProfiles => _profileStore is not null && _filePicker is not null
                                     && _dialogService is not null && _secretStore is not null;

    public bool IsTesting => IsTestRunning;

    public string? NameError => string.IsNullOrWhiteSpace(Name) ? "Name is required." : null;

    public string? AddressError => ConnectionValidation.ValidateAddress(Address);

    public string? ConnectTimeoutError => ConnectionValidation.ValidateDuration(ConnectTimeout);

    public string? KeepaliveTimeError => ConnectionValidation.ValidateDuration(KeepaliveTime);

    public string? KeepaliveTimeoutError => ConnectionValidation.ValidateDuration(KeepaliveTimeout);

    private bool CanSave
        => NameError is null && AddressError is null && ConnectTimeoutError is null
           && KeepaliveTimeError is null && KeepaliveTimeoutError is null && LimitsError is null;

    private bool CanTest => AddressError is null && !string.IsNullOrWhiteSpace(Address);

    private bool CanEditProfile => CanManageProfiles && SelectedTlsProfile?.Profile is not null;

    [RelayCommand(CanExecute = nameof(CanManageProfiles))]
    private async Task NewProfile()
    {
        if (!CanManageProfiles)
        {
            return;
        }

        var editor = new TlsProfileEditorViewModel(_filePicker!, _dialogService!, _secretStore!);
        var saved = await _dialogService!.ShowDialogAsync(editor);

        if (saved is not null)
        {
            await _profileStore!.SaveAsync(saved);
            RebuildProfileOptions(saved.Id);
        }
    }

    [RelayCommand(CanExecute = nameof(CanEditProfile))]
    private async Task EditProfile()
    {
        if (!CanManageProfiles || SelectedTlsProfile?.Profile is not { } existing)
        {
            return;
        }

        var editor = new TlsProfileEditorViewModel(_filePicker!, _dialogService!, _secretStore!, existing);
        var saved = await _dialogService!.ShowDialogAsync(editor);

        if (saved is not null)
        {
            await _profileStore!.SaveAsync(saved);
            RebuildProfileOptions(saved.Id);
        }
    }

    [RelayCommand(CanExecute = nameof(CanManageProfiles))]
    private async Task ManageProfiles()
    {
        if (!CanManageProfiles)
        {
            return;
        }

        var keepId = SelectedTlsProfile?.Profile?.Id;
        await _dialogService!.ShowDialogAsync(
            new TlsProfileManagerViewModel(_profileStore!, _filePicker!, _dialogService!, _secretStore!));

        // The manager may have added, edited, or deleted profiles — re-sync the picker.
        RebuildProfileOptions(keepId);
    }

    /// <summary>Reloads the picker from the store, preserving (or moving to) the given profile id.</summary>
    private void RebuildProfileOptions(string? selectedProfileId)
    {
        TlsProfiles.Clear();
        TlsProfiles.Add(new TlsProfileOption(null)); // system default

        foreach (var profile in _profileStore?.Profiles ?? [])
        {
            TlsProfiles.Add(new TlsProfileOption(profile));
        }

        SelectedTlsProfile = TlsProfiles.FirstOrDefault(o => o.Profile?.Id == selectedProfileId) ?? TlsProfiles[0];
    }

    // ── descriptor source (FR-040..047) ──────────────────────────────────────

    public ObservableCollection<DescriptorPathRow> ProtosetRows { get; }

    public ObservableCollection<DescriptorPathRow> ProtoFileRows { get; }

    public ObservableCollection<DescriptorPathRow> ImportPathRows { get; }

    public IReadOnlyList<DescriptorMode> DescriptorModes { get; } =
        [DescriptorMode.Reflection, DescriptorMode.Protoset, DescriptorMode.Proto];

    public bool IsReflectionMode => SelectedDescriptorMode == DescriptorMode.Reflection;

    public bool IsProtosetMode => SelectedDescriptorMode == DescriptorMode.Protoset;

    public bool IsProtoMode => SelectedDescriptorMode == DescriptorMode.Proto;

    /// <summary>Whether file management is wired (the picker is present); false in bare unit ctors.</summary>
    public bool CanPickFiles => _filePicker is not null;

    public bool ShowProtocRemediation => IsProtoMode && ProtocMissing;

    /// <summary>FR-040: states the effective source so the user knows which settings apply.</summary>
    public string EffectiveSourceText => SelectedDescriptorMode switch
    {
        DescriptorMode.Protoset => "Using protoset file(s); reflection is not contacted.",
        DescriptorMode.Proto => "Using .proto compilation (protoc); protoset and reflection are ignored.",
        _ => "Using server reflection."
    };

    /// <summary>FR-047: Core's DoS limits, surfaced read-only.</summary>
    public IReadOnlyList<string> DescriptorLimits { get; } =
    [
        "Max protoset file size: 64 MiB",
        "Max reflection response: 16 MiB",
        "Max file descriptors: 2,048",
        "Max import depth: 128",
        "Max symbols: 65,536"
    ];

    // FR-049: Core's defaults shown as the override watermarks (blank field = keep the default).
    public string DefaultMaxProtosetFileBytes => DescriptorSourceOptions.DefaultMaxProtosetFileBytes.ToString(CultureInfo.InvariantCulture);

    public string DefaultMaxReflectionDescriptorBytes => DescriptorSourceOptions.DefaultMaxReflectionDescriptorBytes.ToString(CultureInfo.InvariantCulture);

    public string DefaultMaxFileDescriptors => DescriptorSourceOptions.DefaultMaxFileDescriptors.ToString(CultureInfo.InvariantCulture);

    public string DefaultMaxDependencyDepth => DescriptorSourceOptions.DefaultMaxDependencyDepth.ToString(CultureInfo.InvariantCulture);

    public string DefaultMaxSymbols => DescriptorSourceOptions.DefaultMaxSymbols.ToString(CultureInfo.InvariantCulture);

    /// <summary>FR-049: the first invalid limit override (must be blank or a positive integer); null when all are valid.</summary>
    public string? LimitsError
    {
        get
        {
            foreach (var (label, text) in new[]
                     {
                         ("Max protoset file size", MaxProtosetFileBytesOverride),
                         ("Max reflection response", MaxReflectionDescriptorBytesOverride),
                         ("Max file descriptors", MaxFileDescriptorsOverride),
                         ("Max import depth", MaxDependencyDepthOverride),
                         ("Max symbols", MaxSymbolsOverride)
                     })
            {
                if (!string.IsNullOrWhiteSpace(text)
                    && !(long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) && v > 0))
                {
                    return $"{label} must be a positive whole number.";
                }
            }

            return null;
        }
    }

    partial void OnSelectedDescriptorModeChanged(DescriptorMode value)
    {
        if (value == DescriptorMode.Proto)
        {
            _ = DetectProtocAsync();
        }
        else
        {
            ProtocMissing = false;
            ProtocStatus = null;
        }
    }

    private async Task DetectProtocAsync()
    {
        if (_protocService is null)
        {
            return;
        }

        var info = await _protocService.DetectAsync();
        ProtocMissing = !info.Found;
        ProtocStatus = info.Message;
    }

    [RelayCommand(CanExecute = nameof(CanPickFiles))]
    private async Task AddProtosets()
    {
        foreach (var path in await _filePicker!.OpenFilesAsync("Select protoset file(s)", ["protoset", "bin"]))
        {
            ProtosetRows.Add(DescriptorPathRow.ForProtoset(path));
        }
    }

    [RelayCommand(CanExecute = nameof(CanPickFiles))]
    private async Task AddProtoFiles()
    {
        foreach (var path in await _filePicker!.OpenFilesAsync("Select .proto file(s)", ["proto"]))
        {
            ProtoFileRows.Add(DescriptorPathRow.ForProtoFile(path));
        }
    }

    [RelayCommand(CanExecute = nameof(CanPickFiles))]
    private async Task AddImportPath()
    {
        if (await _filePicker!.OpenFolderAsync("Select an import directory") is { } dir)
        {
            ImportPathRows.Add(DescriptorPathRow.ForImportPath(dir));
        }
    }

    [RelayCommand]
    private void RemoveDescriptorRow(DescriptorPathRow? row)
    {
        if (row is null)
        {
            return;
        }

        ProtosetRows.Remove(row);
        ProtoFileRows.Remove(row);
        ImportPathRows.Remove(row);
    }

    [RelayCommand]
    private void MoveDescriptorRowUp(DescriptorPathRow? row) => Move(row, -1);

    [RelayCommand]
    private void MoveDescriptorRowDown(DescriptorPathRow? row) => Move(row, +1);

    /// <summary>FR-044 remediation: switch a protoc-less Proto config to a protoset instead.</summary>
    [RelayCommand]
    private void SwitchToProtoset() => SelectedDescriptorMode = DescriptorMode.Protoset;

    private void Move(DescriptorPathRow? row, int delta)
    {
        if (row is null)
        {
            return;
        }

        foreach (var collection in new[] { ProtosetRows, ProtoFileRows, ImportPathRows })
        {
            var index = collection.IndexOf(row);
            var target = index + delta;

            if (index >= 0 && target >= 0 && target < collection.Count)
            {
                collection.Move(index, target);
                return;
            }
        }
    }

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

        // FR-039: re-validate referenced files exist before probing — a moved/renamed cert, key, protoset,
        // or .proto would otherwise surface as an opaque Core failure deep into the connect.
        if (FirstMissingPath() is { } missing)
        {
            LastTestResult = TestConnectionResult.Failure($"{missing.Label} not found at '{missing.Path}'.");
            IsTestRunning = false;
            return;
        }

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

    /// <summary>
    ///     FR-039: the first referenced file/directory that no longer exists, by current mode. TLS material
    ///     applies only under TLS; descriptor paths apply only in their respective source mode.
    /// </summary>
    private (string Label, string Path)? FirstMissingPath()
    {
        if (!IsPlaintext && SelectedTlsProfile?.Profile is { } tls)
        {
            foreach (var (label, path) in new[]
                     {
                         ("CA certificate", tls.CaCertPath),
                         ("Client certificate", tls.ClientCertPath),
                         ("Client key", tls.ClientKeyPath)
                     })
            {
                if (!string.IsNullOrWhiteSpace(path) && !File.Exists(path))
                {
                    return (label, path);
                }
            }
        }

        if (SelectedDescriptorMode == DescriptorMode.Protoset)
        {
            foreach (var row in ProtosetRows)
            {
                if (!File.Exists(row.Path))
                {
                    return ("Protoset file", row.Path);
                }
            }
        }
        else if (SelectedDescriptorMode == DescriptorMode.Proto)
        {
            foreach (var row in ProtoFileRows)
            {
                if (!File.Exists(row.Path))
                {
                    return (".proto file", row.Path);
                }
            }

            foreach (var row in ImportPathRows)
            {
                if (!Directory.Exists(row.Path))
                {
                    return ("Import directory", row.Path);
                }
            }
        }

        return null;
    }

    [RelayCommand(CanExecute = nameof(CanSave))]
    private void Save() => Close(BuildConnection());

    [RelayCommand]
    private void Cancel() => Close(null);

    public SavedConnection BuildConnection()
    {
        // Sync the descriptor-source config from the editor's mode + ordered path rows.
        _descriptorSource.Mode = SelectedDescriptorMode;
        _descriptorSource.ProtosetPaths = ProtosetRows.Select(r => r.Path).ToList();
        _descriptorSource.ProtoFiles = ProtoFileRows.Select(r => r.Path).ToList();
        _descriptorSource.ImportPaths = ImportPathRows.Select(r => r.Path).ToList();

        // FR-049: persist the limit overrides (blank → null → Core default).
        _descriptorSource.MaxProtosetFileBytes = ParseLong(MaxProtosetFileBytesOverride);
        _descriptorSource.MaxReflectionDescriptorBytes = ParseLong(MaxReflectionDescriptorBytesOverride);
        _descriptorSource.MaxFileDescriptors = ParseInt(MaxFileDescriptorsOverride);
        _descriptorSource.MaxDependencyDepth = ParseInt(MaxDependencyDepthOverride);
        _descriptorSource.MaxSymbols = ParseInt(MaxSymbolsOverride);

        return new SavedConnection
        {
            Id = _id,
            Name = Name.Trim(),
            Address = Address.Trim(),
            Transport = IsPlaintext ? TransportMode.Plaintext : TransportMode.Tls,
            ConnectTimeout = NullIfBlank(ConnectTimeout),
            Keepalive = new KeepaliveSettings { Time = NullIfBlank(KeepaliveTime), Timeout = NullIfBlank(KeepaliveTimeout) },
            Authority = NullIfBlank(Authority),
            ServerName = NullIfBlank(ServerName),
            // A profile reference is meaningful only under TLS; a plaintext target carries none.
            TlsProfileId = IsPlaintext ? null : SelectedTlsProfile?.Profile?.Id,
            UserAgent = NullIfBlank(UserAgent),
            ReflectionHeaders = ReflectionHeaders
                .Where(h => !string.IsNullOrWhiteSpace(h.Name))
                .Select(h => h.ToEntry())
                .ToList(),
            DescriptorSource = _descriptorSource.Clone(),
            Notes = NullIfBlank(Notes)
        };
    }

    private static string? NullIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;

    private static string OverrideText(long? value) => value?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;

    private static long? ParseLong(string text)
        => long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) && v > 0 ? v : null;

    private static int? ParseInt(string text)
        => int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) && v > 0 ? v : null;
}
