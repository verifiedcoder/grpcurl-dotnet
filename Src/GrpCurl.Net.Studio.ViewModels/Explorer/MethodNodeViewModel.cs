using System.Windows.Input;
using GrpCurl.Net.Studio.ViewModels.Models.Descriptors;

namespace GrpCurl.Net.Studio.ViewModels.Explorer;

/// <summary>A method leaf in the explorer tree: name, streaming-shape badge, and signature (FR-020/021).</summary>
public sealed class MethodNodeViewModel : ViewModelBase
{
    public MethodNodeViewModel(ServiceMethod method, ICommand copyFullNameCommand, ICommand describeCommand)
    {
        Method = method;
        CopyFullNameCommand = copyFullNameCommand;
        DescribeCommand = describeCommand;
        Badge = method.Shape.Badge();
        ShapeLabel = method.Shape.Label();
    }

    public ServiceMethod Method { get; }

    /// <summary>Shared explorer commands, carried on the node so the context menu binds directly.</summary>
    public ICommand CopyFullNameCommand { get; }

    public ICommand DescribeCommand { get; }

    public string Name => Method.Name;

    /// <summary>Invocation grammar name <c>pkg.Service/Method</c> (used by Copy full name).</summary>
    public string FullName => Method.FullName;

    public string Badge { get; }

    public string ShapeLabel { get; }

    /// <summary>Compact request → response signature, shown as the node tooltip.</summary>
    public string Signature => $"{Method.InputType} → {Method.OutputType}";
}
