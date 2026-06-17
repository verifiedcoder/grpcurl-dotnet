using CommunityToolkit.Mvvm.Input;
using GrpCurl.Net.Studio.ViewModels.Documents;
using GrpCurl.Net.Studio.ViewModels.Models.Connections;
using GrpCurl.Net.Studio.ViewModels.Services;

namespace GrpCurl.Net.Studio.ViewModels.Connections;

/// <summary>
///     A saved request as it appears in the sidebar under its connection (FR-145): openable, renameable,
///     deletable, and duplicable. The open callback is supplied by the pane; rename/delete/duplicate go
///     through <see cref="ISavedRequestStore" /> directly, whose <c>Changed</c> event refreshes the sidebar.
/// </summary>
public sealed partial class SavedRequestItemViewModel : ViewModelBase
{
    private readonly Func<SavedRequest, Task> _open;
    private readonly ISavedRequestStore? _store;
    private readonly IDialogService? _dialogs;

    public SavedRequestItemViewModel(
        SavedRequest request, Func<SavedRequest, Task> open, ISavedRequestStore? store = null, IDialogService? dialogs = null)
    {
        Request = request;
        _open = open;
        _store = store;
        _dialogs = dialogs;
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

    /// <summary>Whether the manage actions (rename/delete/duplicate) are available (the store is wired).</summary>
    public bool CanManage => _store is not null;

    [RelayCommand]
    private Task Open() => _open(Request);

    [RelayCommand(CanExecute = nameof(CanManage))]
    private async Task Rename()
    {
        if (_store is null || _dialogs is null)
        {
            return;
        }

        var name = await _dialogs.ShowDialogAsync(new TextInputDialogViewModel("Rename request", "Name", Request.Name));

        if (!string.IsNullOrWhiteSpace(name) && name != Request.Name)
        {
            var renamed = Request.Copy();
            renamed.Name = name;
            await _store.SaveAsync(renamed);
        }
    }

    [RelayCommand(CanExecute = nameof(CanManage))]
    private async Task Duplicate()
    {
        if (_store is null)
        {
            return;
        }

        var copy = Request.Copy();
        copy.Id = Guid.NewGuid().ToString();
        copy.Name = $"{Request.Name} (copy)";
        await _store.SaveAsync(copy);
    }

    [RelayCommand(CanExecute = nameof(CanManage))]
    private async Task Delete()
    {
        if (_store is null || _dialogs is null)
        {
            return;
        }

        if (await _dialogs.ConfirmAsync("Delete request", $"Delete saved request '{Request.Name}'? This cannot be undone."))
        {
            await _store.DeleteAsync(Request.Id);
        }
    }
}
