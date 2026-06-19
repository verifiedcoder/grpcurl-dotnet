using System.Collections.ObjectModel;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;

namespace GrpCurl.Net.Studio.ViewModels.Explorer;

/// <summary>A service node in the explorer tree: fully-qualified name with its method leaves (FR-020).</summary>
public sealed partial class ServiceNodeViewModel : ViewModelBase
{
    [ObservableProperty]
    public partial bool IsExpanded { get; set; }

    public ServiceNodeViewModel(string fullName, IReadOnlyList<MethodNodeViewModel> methods, ICommand copyFullNameCommand, ICommand describeCommand, ICommand copyProtoCommand, bool deprecated = false)
    {
        FullName = fullName;
        Deprecated = deprecated;
        CopyFullNameCommand = copyFullNameCommand;
        DescribeCommand = describeCommand;
        CopyProtoCommand = copyProtoCommand;
        Methods = new ObservableCollection<MethodNodeViewModel>(methods);
    }

    public string FullName { get; }

    /// <summary>FR-059: the service carries <c>option deprecated = true</c>.</summary>
    public bool Deprecated { get; }

    /// <summary>Shared explorer commands, carried on the node so the context menu binds directly.</summary>
    public ICommand CopyFullNameCommand { get; }

    public ICommand DescribeCommand { get; }

    /// <summary>FR-054: copy the service's defining file as a .proto snippet.</summary>
    public ICommand CopyProtoCommand { get; }

    public ObservableCollection<MethodNodeViewModel> Methods { get; }

    /// <summary>Per-service method count badge (FR-029).</summary>
    public int MethodCount => Methods.Count;
}
