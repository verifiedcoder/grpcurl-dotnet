using CommunityToolkit.Mvvm.Input;
using GrpCurl.Net.Studio.ViewModels.Models.Connections;

namespace GrpCurl.Net.Studio.ViewModels.Connections;

/// <summary>
///     A saved request as it appears in the sidebar under its connection (FR-145). Carries the open callback
///     the pane supplies, so the view can bind the open action directly on the item.
/// </summary>
public sealed partial class SavedRequestItemViewModel : ViewModelBase
{
    private readonly Func<SavedRequest, Task> _open;

    public SavedRequestItemViewModel(SavedRequest request, Func<SavedRequest, Task> open)
    {
        Request = request;
        _open = open;
    }

    public SavedRequest Request { get; }

    public string Name => Request.Name;

    /// <summary>The bare method name (e.g. <c>SayHello</c>) for a compact secondary line.</summary>
    public string MethodShortName
    {
        get
        {
            var method = Request.Method;
            var slash = method.LastIndexOf('/');
            return slash >= 0 && slash < method.Length - 1 ? method[(slash + 1)..] : method;
        }
    }

    [RelayCommand]
    private Task Open() => _open(Request);
}
