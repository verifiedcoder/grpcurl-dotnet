using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GrpCurl.Net.Studio.ViewModels.Services;

namespace GrpCurl.Net.Studio.ViewModels;

/// <summary>One masked-input row for supplying a missing secret on import (SEC-041); reveal is per-field.</summary>
public sealed partial class ImportSecretRowViewModel : ViewModelBase
{
    public ImportSecretRowViewModel(string displayName, string keyRef)
    {
        DisplayName = displayName;
        KeyRef = keyRef;
    }

    public string DisplayName { get; }

    public string KeyRef { get; }

    [ObservableProperty]
    private string _value = string.Empty;

    [ObservableProperty]
    private bool _isRevealed;
}

/// <summary>
///     SEC-041: on workspace import, lists the secrets whose keyrefs are absent locally and offers a masked
///     input to supply each. Closes with a keyref→value map of the values the user entered (blank rows omitted)
///     via <see cref="ApplyCommand" />, or <see langword="null" /> via <see cref="SkipCommand" /> — the import
///     proceeds either way; skipped secrets can be entered later in the relevant editor.
/// </summary>
public sealed partial class ImportSecretsDialogViewModel : DialogViewModel<IReadOnlyDictionary<string, string>?>
{
    public ImportSecretsDialogViewModel(IReadOnlyList<MissingSecret> missing)
        => Rows = [.. missing.Select(m => new ImportSecretRowViewModel(m.DisplayName, m.KeyRef))];

    public ObservableCollection<ImportSecretRowViewModel> Rows { get; }

    [RelayCommand]
    private void Apply()
        => Close(Rows.Where(r => !string.IsNullOrEmpty(r.Value)).ToDictionary(r => r.KeyRef, r => r.Value));

    [RelayCommand]
    private void Skip() => Close(null);
}
