using GrpCurl.Net.Studio.Tests.Unit.Fakes;
using GrpCurl.Net.Studio.TestSupport;
using GrpCurl.Net.Studio.ViewModels;
using GrpCurl.Net.Studio.ViewModels.Documents;
using GrpCurl.Net.Studio.ViewModels.Models;
using GrpCurl.Net.Studio.ViewModels.Panes;
using GrpCurl.Net.Studio.ViewModels.Services;

namespace GrpCurl.Net.Studio.Tests.Unit;

public sealed class MainWindowViewModelTests
{
    private static ConnectionsPaneViewModel EmptyConnectionsPane()
        => new(new FakeWorkspaceStore(), new FakeConnectionRegistry(), new FakeDialogService(), new ConnectionSelection());

    private static ServiceExplorerViewModel EmptyExplorer()
        => new(new FakeDescriptorService(), new ConnectionSelection(), new FakeClipboardService(), new ImmediateUiDispatcher(), new FakeDocumentHost());

    private static DocumentsViewModel EmptyDocuments()
        => new(new FakeDescriptorService(), new ImmediateUiDispatcher(), new FakeClipboardService());

    private static MainWindowViewModel CreateViewModel(FakeSettingsStore? settings = null)
        => new(
            settings ?? new FakeSettingsStore(),
            EmptyConnectionsPane(),
            EmptyExplorer(),
            new ConsoleViewModel(),
            new InspectorViewModel(),
            EmptyDocuments());

    [Fact]
    public void Construction_exposes_title_and_default_pane_state()
    {
        var vm = CreateViewModel();

        vm.Title.ShouldNotBeNullOrWhiteSpace();
        vm.IsSidebarOpen.ShouldBeTrue();
        vm.IsInspectorOpen.ShouldBeTrue();
        vm.IsConsoleOpen.ShouldBeTrue();
        vm.IsFocusMode.ShouldBeFalse();
        vm.HasAnyConnection.ShouldBeFalse();
    }

    [Fact]
    public void Construction_reflects_persisted_theme()
    {
        var settings = new FakeSettingsStore();
        settings.Current.Appearance.Theme = "dark";

        var vm = CreateViewModel(settings);

        vm.SelectedTheme.ShouldBe(AppTheme.Dark);
    }

    [Fact]
    public void Toggle_sidebar_flips_state()
    {
        var vm = CreateViewModel();

        vm.ToggleSidebarCommand.Execute(null);

        vm.IsSidebarOpen.ShouldBeFalse();
    }

    [Fact]
    public void Focus_mode_collapses_all_panes_then_restores_prior_state()
    {
        var vm = CreateViewModel();

        vm.ToggleInspectorCommand.Execute(null); // inspector closed before focus mode
        vm.IsInspectorOpen.ShouldBeFalse();

        vm.ToggleFocusModeCommand.Execute(null);

        vm.IsFocusMode.ShouldBeTrue();
        vm.IsSidebarOpen.ShouldBeFalse();
        vm.IsInspectorOpen.ShouldBeFalse();
        vm.IsConsoleOpen.ShouldBeFalse();

        vm.ToggleFocusModeCommand.Execute(null);

        vm.IsFocusMode.ShouldBeFalse();
        vm.IsSidebarOpen.ShouldBeTrue();
        vm.IsInspectorOpen.ShouldBeFalse(); // restored to its pre-focus value
        vm.IsConsoleOpen.ShouldBeTrue();
    }

    [Fact]
    public async Task Set_theme_updates_selection_and_persists()
    {
        var settings = new FakeSettingsStore();
        var vm = CreateViewModel(settings);

        await vm.SetThemeCommand.ExecuteAsync(AppTheme.Dark);

        vm.SelectedTheme.ShouldBe(AppTheme.Dark);
        settings.Current.Appearance.Theme.ShouldBe("dark");
        settings.SaveCount.ShouldBe(1);
    }
}
