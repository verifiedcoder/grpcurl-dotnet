using System.Runtime.CompilerServices;
using Avalonia.Controls;
using GrpCurl.Net.Studio;
using GrpCurl.Net.Studio.Tests.UI.Headless;
using GrpCurl.Net.Studio.ViewModels;
using GrpCurl.Net.Studio.ViewModels.Connections;
using GrpCurl.Net.Studio.ViewModels.Documents;
using GrpCurl.Net.Studio.ViewModels.Explorer;

namespace GrpCurl.Net.Studio.Tests.UI;

/// <summary>
///     Reflection-driven guard (SPEC-070 §5): every content view model must resolve to a real
///     view through the <see cref="ViewLocator" />. This makes "forgot to add the View" a CI
///     failure rather than a blank pane at runtime.
/// </summary>
public sealed class ViewLocatorTests(HeadlessSessionFixture fixture) : HeadlessTestBase(fixture)
{
    // View models not resolved through the ViewLocator:
    //  - the shell window is hosted directly by the desktop lifetime (it IS a Window);
    //  - list-item / row view models are rendered via inline DataTemplates inside their
    //    ItemsControls, not by name convention.
    private static readonly HashSet<Type> NotViewLocated =
    [
        typeof(MainWindowViewModel),
        typeof(ConnectionListItemViewModel),
        typeof(HeaderRowViewModel),
        typeof(ServiceNodeViewModel),
        typeof(MethodNodeViewModel),
        typeof(TypePackageNodeViewModel),
        typeof(TypeLeafNodeViewModel),
        typeof(DocumentsViewModel),
        typeof(StreamComposerViewModel), // sub-view-models rendered inline inside the invocation tab
        typeof(StreamLogViewModel),
        typeof(StreamRowViewModel)
    ];

    public static TheoryData<Type> ContentViewModels()
    {
        var data = new TheoryData<Type>();

        foreach (var type in typeof(ViewModelBase).Assembly.GetTypes()
                     .Where(t => t is { IsAbstract: false, IsClass: true }
                                 && typeof(ViewModelBase).IsAssignableFrom(t)
                                 && !NotViewLocated.Contains(t)))
        {
            data.Add(type);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(ContentViewModels))]
    public Task Every_content_view_model_resolves_to_a_view(Type viewModelType) => RunOnUiThread(() =>
    {
        // Construct without invoking the ctor — the locator only reads the runtime type, and
        // content view models may require services to construct.
        var viewModel = RuntimeHelpers.GetUninitializedObject(viewModelType);
        var locator = new ViewLocator();

        locator.Match(viewModel).ShouldBeTrue();

        var view = locator.Build(viewModel);

        // The locator returns a TextBlock fallback ("View not found: ...") when the view is
        // missing; a real view is any other control type.
        view.ShouldNotBeNull();
        var expectedViewName = viewModelType.FullName!
            .Replace(".ViewModels.", ".Views.", StringComparison.Ordinal)
            .Replace("ViewModel", "View", StringComparison.Ordinal);
        view.GetType().FullName.ShouldBe(expectedViewName);
    });
}
