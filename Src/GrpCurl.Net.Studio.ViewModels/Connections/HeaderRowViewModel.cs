using CommunityToolkit.Mvvm.ComponentModel;
using GrpCurl.Net.Studio.ViewModels.Models.Connections;

namespace GrpCurl.Net.Studio.ViewModels.Connections;

/// <summary>An editable metadata-header row (name/value); <c>-bin</c> names mark binary metadata.</summary>
public sealed partial class HeaderRowViewModel : ViewModelBase
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsBin))]
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

    public HeaderEntry ToEntry() => new() { Name = Name, Value = Value, IsBin = IsBin };
}
