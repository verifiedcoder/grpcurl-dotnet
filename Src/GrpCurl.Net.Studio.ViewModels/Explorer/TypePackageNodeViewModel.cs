using System.Collections.ObjectModel;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using GrpCurl.Net.Studio.ViewModels.Models.Descriptors;

namespace GrpCurl.Net.Studio.ViewModels.Explorer;

/// <summary>A package grouping in the explorer's Types branch (FR-022).</summary>
public sealed partial class TypePackageNodeViewModel : ViewModelBase
{
    [ObservableProperty]
    private bool _isExpanded;

    public TypePackageNodeViewModel(string package, IReadOnlyList<TypeLeafNodeViewModel> types)
    {
        Package = package;
        Types = new ObservableCollection<TypeLeafNodeViewModel>(types);
    }

    /// <summary>Package name, or "(default)" for the unnamed package.</summary>
    public string Package { get; }

    public ObservableCollection<TypeLeafNodeViewModel> Types { get; }

    public int TypeCount => Types.Count;
}

/// <summary>A message or enum leaf in the explorer's Types branch (FR-022).</summary>
public sealed class TypeLeafNodeViewModel : ViewModelBase
{
    public TypeLeafNodeViewModel(TypeEntry type, ICommand describeCommand, ICommand copyFullNameCommand, ICommand copyProtoCommand)
    {
        FullName = type.FullName;
        Kind = type.Kind;
        Deprecated = type.Deprecated;
        DescribeCommand = describeCommand;
        CopyFullNameCommand = copyFullNameCommand;
        CopyProtoCommand = copyProtoCommand;

        var lastDot = type.FullName.LastIndexOf('.');
        Name = lastDot >= 0 ? type.FullName[(lastDot + 1)..] : type.FullName;
        Badge = type.Kind == TypeNodeKind.Enum ? "E" : "M";
    }

    public string FullName { get; }

    public string Name { get; }

    public TypeNodeKind Kind { get; }

    /// <summary>FR-059: the type carries <c>option deprecated = true</c>.</summary>
    public bool Deprecated { get; }

    /// <summary>Short badge: M (message) / E (enum).</summary>
    public string Badge { get; }

    public ICommand DescribeCommand { get; }

    public ICommand CopyFullNameCommand { get; }

    /// <summary>FR-054: copy the type's defining file as a .proto snippet.</summary>
    public ICommand CopyProtoCommand { get; }
}
