using System.Collections.ObjectModel;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;

namespace GrpCurl.Net.Studio.ViewModels.Explorer;

/// <summary>A service node in the explorer tree: fully-qualified name with its method leaves (FR-020).</summary>
public sealed partial class ServiceNodeViewModel : ViewModelBase
{
    [ObservableProperty]
    private bool _isExpanded;

    public ServiceNodeViewModel(string fullName, IReadOnlyList<MethodNodeViewModel> methods, ICommand copyFullNameCommand, ICommand describeCommand)
    {
        FullName = fullName;
        CopyFullNameCommand = copyFullNameCommand;
        DescribeCommand = describeCommand;
        Methods = new ObservableCollection<MethodNodeViewModel>(methods);
    }

    public string FullName { get; }

    /// <summary>Shared explorer commands, carried on the node so the context menu binds directly.</summary>
    public ICommand CopyFullNameCommand { get; }

    public ICommand DescribeCommand { get; }

    public ObservableCollection<MethodNodeViewModel> Methods { get; }

    /// <summary>Per-service method count badge (FR-029).</summary>
    public int MethodCount => Methods.Count;
}
