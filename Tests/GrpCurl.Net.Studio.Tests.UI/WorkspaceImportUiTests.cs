using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using GrpCurl.Net.Studio.Tests.UI.Headless;
using GrpCurl.Net.Studio.ViewModels;
using GrpCurl.Net.Studio.ViewModels.Models.Connections;
using GrpCurl.Net.Studio.ViewModels.Services;
using GrpCurl.Net.Studio.Views;

namespace GrpCurl.Net.Studio.Tests.UI;

public sealed class WorkspaceImportUiTests(HeadlessSessionFixture fixture) : HeadlessTestBase(fixture)
{
    private sealed class Preparation : WorkspacePreparation
    {
        public override string Path => "/workspaces/team/shared.gcnws.json";
        public override IReadOnlyList<WorkspaceFileReference> References => [];
        public override WorkspaceModel Workspace => new()
        {
            Connections = [new SavedConnection { Name = "Staging API", TlsProfileId = "staging-tls" }],
            TlsProfiles =
            [
                new TlsProfile { Id = "staging-tls", Name = "Staging TLS", InsecureSkipVerify = true, CaCertPath = "/shared/certs/team-ca.pem", ClientCertPasswordSecretRef = "password" },
                new TlsProfile { Id = "diagnostic-tls", Name = "Staging TLS", InsecureSkipVerify = true },
                new TlsProfile { Id = "custom-ca", Name = "Verified custom CA", CaCertPath = "/shared/certs/verified-ca.pem" },
                new TlsProfile { Id = "safe", Name = "System roots" }
            ]
        };
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public Task Rendered_dialog_starts_unchecked_and_import_returns_only_checked_source_IDs(int count) => RunOnUiThread(() =>
    {
        var preparation = new Preparation();
        var vm = new WorkspaceImportDialogViewModel(preparation, new WorkspaceModel());
        WorkspaceImportDecision? result = null;
        vm.CloseRequested += value => result = value;
        var window = new Window { Content = new WorkspaceImportDialogView { DataContext = vm }, Width = 740, Height = 700 };
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            var checks = window.GetVisualDescendants().OfType<CheckBox>().Where(c => c.IsVisible).ToArray();
            checks.Length.ShouldBe(2);
            checks.ShouldAllBe(c => c.IsChecked == false);
            checks.Select(c => c.GetValue(Avalonia.Automation.AutomationProperties.NameProperty))
                .ShouldBe(["Disable certificate verification for 'Staging TLS'", "Disable certificate verification for 'Staging TLS (imported)'"]);
            var text = string.Join("\n", window.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text));
            text.ShouldContain("turns off all certificate checks");
            text.ShouldContain("test or diagnostic targets");
            text.ShouldContain("ignored when certificate verification is disabled");
            text.ShouldContain("/shared/certs/team-ca.pem");
            text.ShouldContain("Source ID: staging-tls");
            text.ShouldContain("Source ID: diagnostic-tls");
            text.ShouldContain("secret value(s)");
            var import = Button(window, "Import workspace");
            import.IsEffectivelyEnabled.ShouldBeTrue();
            if (count == 0) { Capture(window, "workspace-import-tls-defaults.png"); }
            foreach (var check in checks.Take(count))
            {
                check.BringIntoView();
                _ = check.Focus();
                check.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Space });
                check.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyUpEvent, Key = Key.Space });
                Dispatcher.UIThread.RunJobs();
            }
            vm.TlsProfiles.Count(p => p.DisableCertificateVerification).ShouldBe(count);
            vm.Summary.TlsSecurity.Count(e => e.SelectedBypass).ShouldBe(count);
            window.GetVisualDescendants().OfType<TextBlock>().Count(t => t.Text == "Certificate verification will be disabled.").ShouldBe(count);
            if (count == 1) { Capture(window, "workspace-import-tls-selected.png"); }
            Click(import);
            result.ShouldNotBeNull().ApprovedBypassProfileIds.ShouldBe(new[] { "staging-tls", "diagnostic-tls" }.Take(count));
            result.ValidateFor(preparation);
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task Cancel_discards_checked_choices_and_cancels_the_entire_import() => RunOnUiThread(() =>
    {
        var vm = new WorkspaceImportDialogViewModel(new Preparation(), new WorkspaceModel());
        var closed = false;
        vm.CloseRequested += value => { value.ShouldBeNull(); closed = true; };
        var window = new Window { Content = new WorkspaceImportDialogView { DataContext = vm }, Width = 740, Height = 700 };
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            window.GetVisualDescendants().OfType<CheckBox>().First().IsChecked = true;
            Dispatcher.UIThread.RunJobs();
            Click(Button(window, "Cancel workspace import"));
            closed.ShouldBeTrue();
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
        if (Environment.GetEnvironmentVariable("GRPCN_IMPORT_SCREENSHOT_DIR") is not { Length: > 0 } directory) { return; }
        _ = Directory.CreateDirectory(directory);
        window.UpdateLayout();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
        using var bitmap = window.CaptureRenderedFrame().ShouldNotBeNull();
        bitmap.Save(Path.Combine(directory, name));
    }
}
