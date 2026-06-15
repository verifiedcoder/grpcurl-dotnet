using Avalonia;
using Avalonia.Controls;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using GrpCurl.Net.Studio.Services;
using GrpCurl.Net.Studio.TestSupport;
using GrpCurl.Net.Studio.Tests.UI.Headless;
using GrpCurl.Net.Studio.ViewModels;
using GrpCurl.Net.Studio.ViewModels.Documents;
using GrpCurl.Net.Studio.ViewModels.Panes;
using GrpCurl.Net.Studio.ViewModels.Services;
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
            new ConnectionsPaneViewModel(new FakeWorkspaceStore(), new FakeConnectionRegistry(), new FakeDialogService(), new ConnectionSelection()),
            new ServiceExplorerViewModel(new FakeDescriptorService(), new ConnectionSelection(), new FakeClipboardService(), new ImmediateUiDispatcher(), new FakeDocumentHost()),
            new ConsoleViewModel(),
            new InspectorViewModel(),
            new DocumentsViewModel(new FakeDescriptorService(), new ImmediateUiDispatcher(), new FakeClipboardService(), new FakeInvocationRunner()));

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
    public Task Welcome_add_button_is_bound_to_the_add_connection_command() => RunOnUiThread(() =>
    {
        var pane = new ConnectionsPaneViewModel(
            new FakeWorkspaceStore(), new FakeConnectionRegistry(), new FakeDialogService(), new ConnectionSelection());
        var viewModel = new MainWindowViewModel(
            new InMemorySettingsStore(), pane,
            new ServiceExplorerViewModel(new FakeDescriptorService(), new ConnectionSelection(), new FakeClipboardService(), new ImmediateUiDispatcher(), new FakeDocumentHost()),
            new ConsoleViewModel(), new InspectorViewModel(),
            new DocumentsViewModel(new FakeDescriptorService(), new ImmediateUiDispatcher(), new FakeClipboardService(), new FakeInvocationRunner()));

        var window = new MainWindow { DataContext = viewModel };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        // With no connections the welcome state is shown; its button must invoke the add flow.
        viewModel.HasAnyConnection.ShouldBeFalse();

        var addButton = window.GetVisualDescendants()
            .OfType<Button>()
            .Single(b => Equals(b.Content, "Add Connection"));

        addButton.IsEffectivelyEnabled.ShouldBeTrue();
        addButton.Command.ShouldBeSameAs(pane.AddConnectionCommand);
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
