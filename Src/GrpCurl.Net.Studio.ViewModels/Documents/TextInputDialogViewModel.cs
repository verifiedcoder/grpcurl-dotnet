using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace GrpCurl.Net.Studio.ViewModels.Documents;

/// <summary>
///     A minimal one-line text-input dialog (e.g. naming a saved request, FR-078; renaming, FR-145). Hosted
///     modally; closes with the trimmed value, or <see langword="null" /> on cancel or an empty value.
/// </summary>
public sealed partial class TextInputDialogViewModel : DialogViewModel<string?>
{
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AcceptCommand))]
    private string _value;

    public TextInputDialogViewModel(string title, string prompt, string? initialValue = null)
    {
        Title = title;
        Prompt = prompt;
        _value = initialValue ?? string.Empty;
    }

    public string Title { get; }

    public string Prompt { get; }

    private bool CanAccept => !string.IsNullOrWhiteSpace(Value);

    [RelayCommand(CanExecute = nameof(CanAccept))]
    private void Accept() => Close(Value.Trim());

    [RelayCommand]
    private void Cancel() => Close(null);
}
