using GrpCurl.Net.Studio.Tests.Unit.Fakes;
using GrpCurl.Net.Studio.ViewModels;

namespace GrpCurl.Net.Studio.Tests.Unit;

public sealed class MainWindowViewModelTests
{
    private static MainWindowViewModel CreateViewModel()
        => new(new ImmediateUiDispatcher(), new FakeSettingsStore());

    [Fact]
    public void Construction_resolves_dependencies_and_exposes_title()
    {
        var vm = CreateViewModel();

        vm.Title.ShouldNotBeNullOrWhiteSpace();
        vm.IsSidebarOpen.ShouldBeTrue();
        vm.IsInspectorOpen.ShouldBeTrue();
        vm.IsConsoleOpen.ShouldBeTrue();
        vm.IsFocusMode.ShouldBeFalse();
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
}
