using Avalonia;
using Avalonia.Controls;
using Avalonia.Styling;
using Avalonia.Threading;
using GrpCurl.Net.Studio.Services;
using GrpCurl.Net.Studio.Tests.UI.Headless;
using GrpCurl.Net.Studio.ViewModels;
using GrpCurl.Net.Studio.ViewModels.Panes;
using GrpCurl.Net.Studio.Views;

namespace GrpCurl.Net.Studio.Tests.UI;

/// <summary>
///     Proves the shell renders headless on every CI OS and that theme switching applies live —
///     the E0.2 acceptance criteria.
/// </summary>
public sealed class ShellRenderTests(HeadlessSessionFixture fixture) : HeadlessTestBase(fixture)
{
    private static MainWindowViewModel CreateViewModel()
        => new(
            new InMemorySettingsStore(),
            new ConnectionsPaneViewModel(),
            new ServiceExplorerViewModel(),
            new ConsoleViewModel(),
            new InspectorViewModel());

    [Fact]
    public Task Shell_renders_all_named_zones_with_title() => RunOnUiThread(() =>
    {
        var window = new MainWindow { DataContext = CreateViewModel() };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        window.Title.ShouldNotBeNullOrWhiteSpace();
        window.IsVisible.ShouldBeTrue();

        window.FindControl<Control>("SidebarZone").ShouldNotBeNull();
        window.FindControl<Control>("CentreZone").ShouldNotBeNull();
        window.FindControl<Control>("InspectorZone").ShouldNotBeNull();
        window.FindControl<Control>("ConsoleZone").ShouldNotBeNull();
        window.FindControl<Control>("Welcome").ShouldNotBeNull();
    });

    [Fact]
    public Task Theme_switch_applies_dark_variant() => RunOnUiThread(() =>
    {
        var application = Application.Current!;

        application.RequestedThemeVariant = ThemeVariant.Dark;
        Dispatcher.UIThread.RunJobs();

        application.ActualThemeVariant.ShouldBe(ThemeVariant.Dark);
    });
}
