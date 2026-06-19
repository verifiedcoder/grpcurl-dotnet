using CommunityToolkit.Mvvm.ComponentModel;

namespace GrpCurl.Net.Studio.ViewModels.Documents;

/// <summary>
///     One row of the quick-vars grid (GQL-018): a declared operation variable with its printed type and
///     required flag, plus an editable value that round-trips to/from the variables JSON.
/// </summary>
public sealed partial class GraphQlVariableRow : ObservableObject
{
    public GraphQlVariableRow(string name, string type, bool required)
    {
        Name = name;
        Type = type;
        Required = required;
    }

    public string Name { get; }

    public string Type { get; }

    public bool Required { get; }

    /// <summary>A " *" suffix on the name for a required variable (no default), otherwise empty.</summary>
    public string RequiredMarker => Required ? " *" : string.Empty;

    /// <summary>The JSON form of the value (e.g. <c>5</c>, <c>"hello"</c>, <c>[1,2]</c>); empty means unset.</summary>
    [ObservableProperty]
    public partial string Value { get; set; } = string.Empty;
}
