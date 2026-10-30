using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GrpCurl.Net.Studio.ViewModels.Services;

namespace GrpCurl.Net.Studio.ViewModels;

public sealed partial class WorkspaceApprovalItemViewModel(WorkspaceFileReference reference) : ViewModelBase
{
    public WorkspaceFileReference Reference { get; } = reference;
    [ObservableProperty]
    public partial bool IsApproved { get; set; }
}

public sealed partial class WorkspaceApprovalDialogViewModel : DialogViewModel<IReadOnlyList<string>?>
{
    public WorkspaceApprovalDialogViewModel(WorkspacePreparation preparation)
    {
        WorkspacePath = preparation.Path;
        Items = preparation.References.Where(r => r.ApprovalReason is not null)
            .Select(r => new WorkspaceApprovalItemViewModel(r)).ToList();
        foreach (var item in Items)
        {
            item.PropertyChanged += (_, _) => ApproveCommand.NotifyCanExecuteChanged();
        }
    }
    public override string Title => "Approve workspace file references";
    public string WorkspacePath { get; }
    public IReadOnlyList<WorkspaceApprovalItemViewModel> Items { get; }
    private bool CanApprove => Items.All(i => i.IsApproved);
    [RelayCommand(CanExecute = nameof(CanApprove))]
    private void Approve() => Close(Items.Select(i => i.Reference.ApprovalKey).ToArray());
    [RelayCommand]
    private void Cancel() => Close(null);
}
