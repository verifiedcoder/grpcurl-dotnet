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
            new FakeThemeService(),
            new ConnectionsPaneViewModel(new FakeWorkspaceStore(), new FakeConnectionRegistry(), new FakeDialogService(), new ConnectionSelection()),
            new ServiceExplorerViewModel(new FakeDescriptorService(), new ConnectionSelection(), new FakeClipboardService(), new ImmediateUiDispatcher(), new FakeDocumentHost()),
            new ConsoleViewModel(),
            new InspectorViewModel(),
            new DocumentsViewModel(new FakeDescriptorService(), new ImmediateUiDispatcher(), new FakeClipboardService(), new FakeInvocationRunner(), new FakeDialogService(), new FakeLauncherService(), new FakeRequestValidator(), new InMemorySettingsStore(), new FakeThemeService()));

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
    public Task Status_bar_shows_the_workspace_file_and_dirty_dot() => RunOnUiThread(() =>
    {
        var store = new FakeWorkspaceStore(new ViewModels.Models.Connections.WorkspaceModel { Id = "w", Name = "Demo" });
        store.SaveAsAsync(store.Current, "/ws/demo.gcnws.json").GetAwaiter().GetResult();
        var session = new WorkspaceSessionViewModel(store, new FakeDialogService());
        var vm = new MainWindowViewModel(
            new FakeThemeService(),
            new ConnectionsPaneViewModel(store, new FakeConnectionRegistry(), new FakeDialogService(), new ConnectionSelection()),
            new ServiceExplorerViewModel(new FakeDescriptorService(), new ConnectionSelection(), new FakeClipboardService(), new ImmediateUiDispatcher(), new FakeDocumentHost()),
            new ConsoleViewModel(), new InspectorViewModel(),
            new DocumentsViewModel(new FakeDescriptorService(), new ImmediateUiDispatcher(), new FakeClipboardService(), new FakeInvocationRunner(), new FakeDialogService(), new FakeLauncherService(), new FakeRequestValidator(), new InMemorySettingsStore(), new FakeThemeService()),
            workspaceStore: store, session: session, filePicker: new FakeFilePickerService(), dialogs: new FakeDialogService());

        var window = new MainWindow { DataContext = vm };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var status = window.GetVisualDescendants().OfType<TextBlock>()
            .Single(t => Equals(t.GetValue(Avalonia.Automation.AutomationProperties.NameProperty), "Workspace status"));
        status.Text.ShouldBe("demo.gcnws.json");

        store.SetDirty(true);
        Dispatcher.UIThread.RunJobs();
        status.Text.ShouldBe("demo.gcnws.json ●");
    });

    [Fact]
    public Task Opening_settings_without_connections_shows_the_tab_not_the_welcome() => RunOnUiThread(() =>
    {
        var vm = CreateViewModel();
        var window = new MainWindow { DataContext = vm };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var tabs = window.FindControl<TabControl>("DocumentTabs")!;
        var welcome = window.FindControl<Control>("Welcome")!;
        tabs.IsVisible.ShouldBeFalse();   // fresh workspace: welcome overlay, no tabs
        welcome.IsVisible.ShouldBeTrue();

        // File → Settings opens a tab even with no connections — it must not stay hidden behind welcome.
        vm.Documents.OpenSettings();
        Dispatcher.UIThread.RunJobs();

        tabs.IsVisible.ShouldBeTrue();
        welcome.IsVisible.ShouldBeFalse();
    });

    [Fact]
    public Task Welcome_add_button_is_bound_to_the_add_connection_command() => RunOnUiThread(() =>
    {
        var pane = new ConnectionsPaneViewModel(
            new FakeWorkspaceStore(), new FakeConnectionRegistry(), new FakeDialogService(), new ConnectionSelection());
        var viewModel = new MainWindowViewModel(
            new FakeThemeService(), pane,
            new ServiceExplorerViewModel(new FakeDescriptorService(), new ConnectionSelection(), new FakeClipboardService(), new ImmediateUiDispatcher(), new FakeDocumentHost()),
            new ConsoleViewModel(), new InspectorViewModel(),
            new DocumentsViewModel(new FakeDescriptorService(), new ImmediateUiDispatcher(), new FakeClipboardService(), new FakeInvocationRunner(), new FakeDialogService(), new FakeLauncherService(), new FakeRequestValidator(), new InMemorySettingsStore(), new FakeThemeService()));

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
