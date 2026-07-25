using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace GrpCurl.Net.Studio.ViewModels.Connections;

/// <summary>
///     FR-126: the delete-connection confirmation, with an optional "also purge this connection's history"
///     checkbox shown when matching history entries exist. Closes with <see langword="true" /> (delete +
///     purge), <see langword="false" /> (delete only), or <see langword="null" /> (cancel).
/// </summary>
public sealed partial class DeleteConnectionDialogViewModel : DialogViewModel<bool?>
{
    public DeleteConnectionDialogViewModel(string connectionName, int historyCount)
    {
        Message = $"Delete '{connectionName}'? This cannot be undone.";
        HistoryCount = historyCount;
        HistoryOptionText = $"Also delete {historyCount} history "
            + (historyCount == 1 ? "entry" : "entries") + " recorded for this connection";
    }

    public override string Title => "Delete connection";

    public string Message { get; }

    public int HistoryCount { get; }

    public bool HasHistory => HistoryCount > 0;

    public string HistoryOptionText { get; }

    [ObservableProperty]
    public partial bool PurgeHistory { get; set; }

    [RelayCommand]
    private void Delete() => Close(PurgeHistory);

    [RelayCommand]
    private void Cancel() => Close(null);
}
