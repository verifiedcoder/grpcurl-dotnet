using Avalonia.Controls;
using Avalonia.Controls.Templates;
using GrpCurl.Net.Studio.ViewModels;

namespace GrpCurl.Net.Studio;

/// <summary>
///     View-first data template (SPEC-030 §3): resolves a <c>FooViewModel</c> to its
///     <c>FooView</c> by convention — swapping the <c>.ViewModels</c> namespace segment for
///     <c>.Views</c> and the <c>ViewModel</c> type suffix for <c>View</c>. Registered in
///     <c>App.axaml</c>'s data templates so any view model rendered as content gets its view.
/// </summary>
public sealed class ViewLocator : IDataTemplate
{
    public Control Build(object? data)
    {
        if (data is null)
        {
            return new TextBlock { Text = "(null)" };
        }

        var viewModelName = data.GetType().FullName!;
        var viewName = viewModelName
            .Replace(".ViewModels.", ".Views.", StringComparison.Ordinal)
            .Replace("ViewModel", "View", StringComparison.Ordinal);

        var viewType = Type.GetType(viewName);

        return viewType is not null
            ? (Control)Activator.CreateInstance(viewType)!
            : new TextBlock { Text = $"View not found: {viewName}" };
    }

    public bool Match(object? data) => data is ViewModelBase;
}
