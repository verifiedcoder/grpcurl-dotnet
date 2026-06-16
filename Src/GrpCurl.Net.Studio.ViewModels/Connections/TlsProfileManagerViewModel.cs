using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using GrpCurl.Net.Studio.ViewModels.Models.Connections;
using GrpCurl.Net.Studio.ViewModels.Services;

namespace GrpCurl.Net.Studio.ViewModels.Connections;

/// <summary>One row in the TLS profile manager: a profile with its live usage count (FR-038).</summary>
public sealed record TlsProfileRow(TlsProfile Profile, int UsageCount)
{
    public string Display => Profile.Name;

    public string UsageText => UsageCount switch
    {
        0 => "unused",
        1 => "used by 1 connection",
        _ => $"used by {UsageCount} connections"
    };
}

/// <summary>
///     Lists workspace TLS profiles and supports create / edit / duplicate / delete (FR-030). Deleting a
///     profile in use warns and lists the affected connections, then reverts them to system-default
///     validation. Closes with <see langword="true" /> when anything changed so the connection editor can
///     refresh its picker.
/// </summary>
public sealed partial class TlsProfileManagerViewModel : DialogViewModel<bool>
{
    private readonly ITlsProfileStore _store;
    private readonly IFilePickerService _filePicker;
    private readonly IDialogService _dialogService;
    private readonly ISecretStore _secretStore;

    private bool _changed;

    public TlsProfileManagerViewModel(
        ITlsProfileStore store, IFilePickerService filePicker, IDialogService dialogService, ISecretStore secretStore)
    {
        _store = store;
        _filePicker = filePicker;
        _dialogService = dialogService;
        _secretStore = secretStore;

        Profiles = [];
        Reload();
    }

    public ObservableCollection<TlsProfileRow> Profiles { get; }

    public bool HasProfiles => Profiles.Count > 0;

    [RelayCommand]
    private async Task NewProfile()
    {
        var saved = await _dialogService.ShowDialogAsync(
            new TlsProfileEditorViewModel(_filePicker, _dialogService, _secretStore));

        if (saved is not null)
        {
            await _store.SaveAsync(saved);
            MarkChangedAndReload();
        }
    }

    [RelayCommand]
    private async Task EditProfile(TlsProfileRow? row)
    {
        if (row is null)
        {
            return;
        }

        var saved = await _dialogService.ShowDialogAsync(
            new TlsProfileEditorViewModel(_filePicker, _dialogService, _secretStore, row.Profile));

        if (saved is not null)
        {
            await _store.SaveAsync(saved);
            MarkChangedAndReload();
        }
    }

    [RelayCommand]
    private async Task DuplicateProfile(TlsProfileRow? row)
    {
        if (row is null)
        {
            return;
        }

        var source = row.Profile;
        var copy = new TlsProfile
        {
            Name = $"{source.Name} (copy)",
            InsecureSkipVerify = source.InsecureSkipVerify,
            CaCertPath = source.CaCertPath,
            ClientCertPath = source.ClientCertPath,
            ClientKeyPath = source.ClientKeyPath,
            RevocationMode = source.RevocationMode,
            ExportableClientKey = source.ExportableClientKey
        };

        // Give the copy its own secret entry so deleting either profile can't strand the other's password.
        if (!string.IsNullOrWhiteSpace(source.ClientCertPasswordSecretRef))
        {
            var password = await _secretStore.GetAsync(source.ClientCertPasswordSecretRef);

            if (password is not null)
            {
                var newRef = Guid.NewGuid().ToString("N");
                await _secretStore.SetAsync(newRef, password);
                copy.ClientCertPasswordSecretRef = newRef;
            }
        }

        await _store.SaveAsync(copy);
        MarkChangedAndReload();
    }

    [RelayCommand]
    private async Task DeleteProfile(TlsProfileRow? row)
    {
        if (row is null)
        {
            return;
        }

        var referencing = _store.ReferencingConnections(row.Profile.Id);
        var message = referencing.Count == 0
            ? $"Delete TLS profile '{row.Profile.Name}'? This cannot be undone."
            : $"Delete TLS profile '{row.Profile.Name}'? {referencing.Count} connection(s) use it "
              + $"({string.Join(", ", referencing)}) and will revert to system-default validation.";

        if (await _dialogService.ConfirmAsync("Delete TLS profile", message))
        {
            await _store.DeleteAsync(row.Profile.Id);
            MarkChangedAndReload();
        }
    }

    [RelayCommand]
    private void Close() => Close(_changed);

    private void MarkChangedAndReload()
    {
        _changed = true;
        Reload();
    }

    private void Reload()
    {
        Profiles.Clear();

        foreach (var profile in _store.Profiles)
        {
            Profiles.Add(new TlsProfileRow(profile, _store.UsageCount(profile.Id)));
        }

        OnPropertyChanged(nameof(HasProfiles));
    }
}
