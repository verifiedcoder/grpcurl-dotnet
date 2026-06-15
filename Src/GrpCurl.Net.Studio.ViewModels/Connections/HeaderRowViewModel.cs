using CommunityToolkit.Mvvm.ComponentModel;
using GrpCurl.Net.Studio.ViewModels.Models.Connections;
using GrpCurl.Net.Utilities;

namespace GrpCurl.Net.Studio.ViewModels.Connections;

/// <summary>An editable metadata-header row (name/value); <c>-bin</c> names mark binary metadata.</summary>
public sealed partial class HeaderRowViewModel : ViewModelBase
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsBin))]
    [NotifyPropertyChangedFor(nameof(IsSecret))]
    private string _name = string.Empty;

    [ObservableProperty]
    private string _value = string.Empty;

    public HeaderRowViewModel()
    {
    }

    public HeaderRowViewModel(HeaderEntry entry)
    {
        _name = entry.Name;
        _value = entry.Value;
    }

    public bool IsBin => Name.EndsWith("-bin", StringComparison.OrdinalIgnoreCase);

    /// <summary>True for sensitive header names (per Core's <see cref="SecretRedactor" />) — masked in the UI (FR-068).</summary>
    public bool IsSecret => SecretRedactor.ShouldRedact(Name);

    public HeaderEntry ToEntry() => new() { Name = Name, Value = Value, IsBin = IsBin };
}
