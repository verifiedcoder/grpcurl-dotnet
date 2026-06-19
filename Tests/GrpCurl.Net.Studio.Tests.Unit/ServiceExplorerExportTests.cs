using GrpCurl.Net.Studio.TestSupport;
using GrpCurl.Net.Studio.ViewModels.Models.Connections;
using GrpCurl.Net.Studio.ViewModels.Models.Descriptors;
using GrpCurl.Net.Studio.ViewModels.Panes;
using GrpCurl.Net.Studio.ViewModels.Services;

namespace GrpCurl.Net.Studio.Tests.Unit;

/// <summary>
///     L1 tests for the explorer's schema-export commands (E2.4): pick → export → reveal, the
///     refuse-by-default overwrite gate + force retry (FR-101/102), and cancel-on-no-pick.
/// </summary>
public sealed class ServiceExplorerExportTests
{
    private static ServiceCatalog NonEmpty() => new(
        [new ServiceEntry("pkg.Greeter", [new ServiceMethod("Hi", "pkg.Greeter/Hi", StreamingShape.Unary, "pkg.A", "pkg.B")])], []);

    private static (ServiceExplorerViewModel Vm, FakeDescriptorService Descriptors, FakeFilePickerService Picker, FakeDialogService Dialog, FakeLauncherService Launcher)
        Loaded()
    {
        var descriptors = new FakeDescriptorService { Result = DescriptorLoadResult.Success(NonEmpty()) };
        var selection = new ConnectionSelection();
        var picker = new FakeFilePickerService();
        var dialog = new FakeDialogService();
        var launcher = new FakeLauncherService();
        var vm = new ServiceExplorerViewModel(
            descriptors, selection, new FakeClipboardService(), new ImmediateUiDispatcher(), new FakeDocumentHost(),
            protoc: null, console: null, picker, dialog, launcher);
        selection.Set(new SavedConnection { Name = "prod", Address = "h:1" });
        return (vm, descriptors, picker, dialog, launcher);
    }

    [Fact]
    public void Export_is_disabled_until_a_schema_is_loaded()
    {
        var vm = new ServiceExplorerViewModel(
            new FakeDescriptorService(), new ConnectionSelection(), new FakeClipboardService(),
            new ImmediateUiDispatcher(), new FakeDocumentHost(), null, null, new FakeFilePickerService(), new FakeDialogService(), new FakeLauncherService());

        vm.CanExport.ShouldBeFalse();
        vm.ExportProtosetCommand.CanExecute(null).ShouldBeFalse();
    }

    [Fact]
    public async Task Export_protoset_picks_a_path_and_reveals_on_success()
    {
        var (vm, descriptors, picker, dialog, launcher) = Loaded();
        vm.CanExport.ShouldBeTrue();
        picker.SaveResult = Path.Combine(Path.GetTempPath(), "schema.protoset");
        descriptors.ExportProtosetResult = SchemaExportResult.Success([new ExportedFile(picker.SaveResult, 128)], TimeSpan.FromMilliseconds(7));
        dialog.ConfirmResult = true; // reveal: yes

        await vm.ExportProtosetCommand.ExecuteAsync(null);

        descriptors.LastExportProtosetPath.ShouldBe(picker.SaveResult);
        picker.LastSaveSuggestedName.ShouldBe("prod.protoset");
        launcher.LaunchCount.ShouldBe(1);
    }

    [Fact]
    public async Task Cancelling_the_picker_skips_the_export()
    {
        var (vm, descriptors, picker, _, launcher) = Loaded();
        picker.SaveResult = null; // user cancelled

        await vm.ExportProtosetCommand.ExecuteAsync(null);

        descriptors.LastExportProtosetPath.ShouldBeNull();
        launcher.LaunchCount.ShouldBe(0);
    }

    [Fact]
    public async Task Declining_the_reveal_does_not_launch()
    {
        var (vm, descriptors, picker, dialog, launcher) = Loaded();
        picker.SaveResult = Path.Combine(Path.GetTempPath(), "x.protoset");
        descriptors.ExportProtosetResult = SchemaExportResult.Success([], TimeSpan.Zero);
        dialog.ConfirmResult = false;

        await vm.ExportProtosetCommand.ExecuteAsync(null);

        launcher.LaunchCount.ShouldBe(0);
    }

    [Fact]
    public async Task A_conflict_prompts_then_force_retries_when_confirmed()
    {
        var (vm, descriptors, picker, dialog, _) = Loaded();
        picker.SaveResult = Path.Combine(Path.GetTempPath(), "y.protoset");
        var overwriteSeen = new List<bool>();
        descriptors.OnExportProtoset = (_, overwrite) =>
        {
            overwriteSeen.Add(overwrite);
            return overwrite
                ? SchemaExportResult.Success([], TimeSpan.Zero)
                : SchemaExportResult.Conflict([new FileConflict(picker.SaveResult!, 10, DateTime.UtcNow)]);
        };
        dialog.ConfirmResult = true; // confirm overwrite (and reveal)

        await vm.ExportProtosetCommand.ExecuteAsync(null);

        overwriteSeen.ShouldBe([false, true]); // refused-by-default, then forced after confirm
    }

    [Fact]
    public async Task A_conflict_declined_does_not_overwrite()
    {
        var (vm, descriptors, picker, dialog, _) = Loaded();
        picker.SaveResult = Path.Combine(Path.GetTempPath(), "z.protoset");
        var overwriteSeen = new List<bool>();
        descriptors.OnExportProtoset = (_, overwrite) =>
        {
            overwriteSeen.Add(overwrite);
            return SchemaExportResult.Conflict([new FileConflict(picker.SaveResult!, 10, DateTime.UtcNow)]);
        };
        dialog.ConfirmResult = false; // decline overwrite

        await vm.ExportProtosetCommand.ExecuteAsync(null);

        overwriteSeen.ShouldBe([false]); // never forced
    }

    [Fact]
    public async Task Export_protos_picks_a_folder()
    {
        var (vm, descriptors, picker, dialog, launcher) = Loaded();
        picker.OpenFolderResult = Path.Combine(Path.GetTempPath(), "protos-out");
        descriptors.ExportProtosResult = SchemaExportResult.Success([new ExportedFile("a.proto", 50)], TimeSpan.FromMilliseconds(3));
        dialog.ConfirmResult = true;

        await vm.ExportProtosCommand.ExecuteAsync(null);

        descriptors.LastExportProtosDirectory.ShouldBe(picker.OpenFolderResult);
        launcher.LaunchCount.ShouldBe(1);
    }
}
