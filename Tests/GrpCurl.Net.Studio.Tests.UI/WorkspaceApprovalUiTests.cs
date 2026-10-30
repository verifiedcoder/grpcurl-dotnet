using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using GrpCurl.Net.Studio.Services;
using GrpCurl.Net.Studio.Tests.UI.Headless;
using GrpCurl.Net.Studio.TestSupport;
using GrpCurl.Net.Studio.ViewModels;
using GrpCurl.Net.Studio.ViewModels.Documents;
using GrpCurl.Net.Studio.ViewModels.Models.Connections;
using GrpCurl.Net.Studio.ViewModels.Services;
using GrpCurl.Net.Studio.Views;
using GrpCurl.Net.Studio.Views.Documents;

namespace GrpCurl.Net.Studio.Tests.UI;

public sealed class WorkspaceApprovalUiTests(HeadlessSessionFixture fixture) : HeadlessTestBase(fixture)
{
    private sealed class Preparation : WorkspacePreparation
    {
        public override WorkspaceModel Workspace => WorkspaceModel.Empty();
        public override string Path => "/workspaces/team/workspace.gcnws.json";
        public override IReadOnlyList<WorkspaceFileReference> References =>
        [
            new("connection", "Staging API", "Descriptor protoset", "../../shared/api.protoset", "/shared/api.protoset", "/shared/api.protoset", false, "Outside the workspace directory. Approval permits access to this location."),
            new("tls", "Client TLS", "TLS client private key", "../../certs/client.key", "/certs/client.key", "/certs/client.key", false, "Outside the workspace directory. Approval permits access to this location.")
        ];
    }

    private sealed class Trust : IWorkspaceTrustStore
    {
        public List<string> Locations { get; } = ["/workspaces/team/workspace.gcnws.json", "/workspaces/demo/workspace.gcnws.json"];
        public IReadOnlyList<string> ApprovedWorkspaces => Locations;
        public string? RecoveryMessage { get; set; }
        public bool Fail { get; set; }
        public void Revoke(string? workspacePath)
        {
            if (Fail) { throw new WorkspaceTrustException("Approval storage is unavailable. Restore directory access and retry."); }
            if (workspacePath is null) { Locations.Clear(); RecoveryMessage = null; }
            else { _ = Locations.Remove(workspacePath); }
        }
    }

    [Fact]
    public Task Rendered_approval_defaults_unchecked_and_requires_every_checkbox() => RunOnUiThread(() =>
    {
        var vm = new WorkspaceApprovalDialogViewModel(new Preparation());
        IReadOnlyList<string>? result = null;
        vm.CloseRequested += value => result = value;
        var window = new Window { Content = new WorkspaceApprovalDialogView { DataContext = vm }, Width = 760, Height = 660 };
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            var checks = window.GetVisualDescendants().OfType<CheckBox>().ToArray();
            checks.Length.ShouldBe(2);
            checks.ShouldAllBe(c => c.IsChecked == false);
            var approve = Button(window, "Approve selected workspace references");
            approve.IsEffectivelyEnabled.ShouldBeFalse();
            Capture(window, "workspace-file-approval.png");
            checks[0].IsChecked = true;
            Dispatcher.UIThread.RunJobs();
            approve.IsEffectivelyEnabled.ShouldBeFalse();
            checks[1].IsChecked = true;
            Dispatcher.UIThread.RunJobs();
            approve.IsEffectivelyEnabled.ShouldBeTrue();
            Click(approve);
            result.ShouldNotBeNull().Count.ShouldBe(2);
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task Cancel_binding_declines_without_returning_approvals() => RunOnUiThread(() =>
    {
        var vm = new WorkspaceApprovalDialogViewModel(new Preparation());
        var closed = false;
        vm.CloseRequested += value => { value.ShouldBeNull(); closed = true; };
        var window = new Window { Content = new WorkspaceApprovalDialogView { DataContext = vm }, Width = 760, Height = 660 };
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            Click(Button(window, "Cancel workspace file approval"));
            closed.ShouldBeTrue();
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task Settings_revokes_selected_and_all_and_surfaces_recovery_errors() => RunOnUiThread(() =>
    {
        var trust = new Trust();
        using var vm = new SettingsDocumentViewModel(new InMemorySettingsStore(), new FakeThemeService(), new FakeDialogService(), workspaceTrust: trust);
        var window = new Window { Content = new SettingsDocumentView { DataContext = vm }, Width = 900, Height = 800 };
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            var revoke = window.GetVisualDescendants().OfType<Button>().First(b => Equals(b.GetValue(Avalonia.Automation.AutomationProperties.NameProperty), "Revoke workspace approval"));
            revoke.BringIntoView();
            Dispatcher.UIThread.RunJobs();
            var scroll = window.GetVisualDescendants().OfType<ScrollViewer>().First();
            var header = window.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Text == "Security");
            var position = header.TranslatePoint(new Point(0, 0), scroll)!.Value;
            scroll.Offset = new Vector(0, scroll.Offset.Y + position.Y - 20);
            Dispatcher.UIThread.RunJobs();
            Capture(window, "workspace-security-settings.png");
            Click(revoke);
            trust.Locations.ShouldBe(["/workspaces/demo/workspace.gcnws.json"]);
            vm.ApprovedWorkspaceLocations.Count.ShouldBe(1);
            trust.Fail = true;
            Click(Button(window, "Revoke all workspace approvals"));
            vm.WorkspaceTrustStatus.ShouldNotBeNull().ShouldContain("Restore directory access");
            trust.Locations.Count.ShouldBe(1);
            trust.Fail = false;
            Click(Button(window, "Revoke all workspace approvals"));
            trust.Locations.ShouldBeEmpty();
            vm.ApprovedWorkspaceLocations.ShouldBeEmpty();
        }
        finally { window.Close(); }
    });

    private static Button Button(Window window, string name) => window.GetVisualDescendants().OfType<Button>()
        .Single(b => Equals(b.GetValue(Avalonia.Automation.AutomationProperties.NameProperty), name));
    private static void Click(Button button)
    {
        button.BringIntoView();
        _ = button.Focus();
        button.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter });
        Dispatcher.UIThread.RunJobs();
    }
    private static void Capture(Window window, string name)
    {
        if (Environment.GetEnvironmentVariable("GRPCN_TRUST_SCREENSHOT_DIR") is not { Length: > 0 } directory) { return; }
        _ = Directory.CreateDirectory(directory);
        using var bitmap = new RenderTargetBitmap(new PixelSize((int)window.Width, (int)window.Height), new Vector(96, 96));
        bitmap.Render(window);
        bitmap.Save(Path.Combine(directory, name));
    }
}
