using System.Collections.ObjectModel;

namespace GrpCurl.Net.Studio.ViewModels.Panes;

/// <summary>
///     Bottom console: an activity log. E2.3 mirrors descriptor-load warnings here (FR-046); richer
///     per-call timing arrives with the verbose/timing panel (E2.5).
/// </summary>
public sealed class ConsoleViewModel : ViewModelBase
{
    public string Header => "Console";

    public ObservableCollection<string> Messages { get; } = [];

    /// <summary>Appends one line to the activity log.</summary>
    public void Append(string message) => Messages.Add(message);
}
