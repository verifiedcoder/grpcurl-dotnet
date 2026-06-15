using Avalonia.Controls;
using GrpCurl.Net.Studio;
using GrpCurl.Net.Studio.Tests.UI.Headless;
using GrpCurl.Net.Studio.ViewModels;

namespace GrpCurl.Net.Studio.Tests.UI;

/// <summary>
///     Reflection-driven guard (SPEC-070 §5): every content view model must resolve to a real
///     view through the <see cref="ViewLocator" />. This makes "forgot to add the View" a CI
///     failure rather than a blank pane at runtime.
/// </summary>
public sealed class ViewLocatorTests(HeadlessSessionFixture fixture) : HeadlessTestBase(fixture)
{
    // The shell window is hosted directly by the desktop lifetime (it IS a Window), not
    // resolved through the ViewLocator, so it is excluded from the convention check.
    private static readonly HashSet<Type> NotViewLocated = [typeof(MainWindowViewModel)];

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
        var viewModel = Activator.CreateInstance(viewModelType)!;
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
