using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using GrpCurl.Net.Studio.Services;
using GrpCurl.Net.Studio.Tests.UI.Headless;
using GrpCurl.Net.Studio.TestSupport;
using GrpCurl.Net.Studio.ViewModels.Documents;
using GrpCurl.Net.Studio.ViewModels.Models;
using GrpCurl.Net.Studio.Views.Documents;

namespace GrpCurl.Net.Studio.Tests.UI;

public sealed class UpdateUrlUiTests(HeadlessSessionFixture fixture) : HeadlessTestBase(fixture)
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task Rejected_update_uses_real_copy_only_prompt(bool confirm) => RunOnUiThread(() =>
    {
        const string url = "https://github.com.evil.example/update";
        var service = new FakeUpdateService { CheckResult = UpdateCheckResult.Available("v999.0.0", url) };
        var launcher = new FakeLauncherService();
        var clipboard = new FakeClipboardService();
        var window = new Window { Width = 900, Height = 800 };
        using var vm = new SettingsDocumentViewModel(new InMemorySettingsStore(), new FakeThemeService(),
            new DialogService(() => window), updates: service, launcher: launcher, clipboard: clipboard);
        window.Content = new SettingsDocumentView { DataContext = vm };
        Window? prompt = null;
        using var opened = Window.WindowOpenedEvent.AddClassHandler<Window>((w, _) =>
        {
            if (w.Title != "Unsupported link") { return; }
            prompt = w;
        });
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            Click(Button(window, "Check for updates"));
            launcher.LaunchCount.ShouldBe(0);
            var open = window.GetVisualDescendants().OfType<Button>()
                .Single(b => ReferenceEquals(b.Command, vm.OpenLatestReleaseCommand));
            open.IsVisible.ShouldBeTrue();
            open.IsEnabled.ShouldBeTrue();
            open.BringIntoView();
            Capture(window, "update-settings.png");
            Click(open);
            var w = prompt.ShouldNotBeNull();
            Dispatcher.UIThread.RunJobs();
            var body = w.GetVisualDescendants().OfType<TextBlock>()
                .Select(t => t.Text).Single(t => t?.Contains("Copy it to the clipboard", StringComparison.Ordinal) == true)!;
            body.ShouldContain("HTTPS update links on github.com or its subdomains");
            body.ShouldContain("Copy it to the clipboard instead?");
            body.ShouldContain(url);
            Capture(w, "update-rejected-link.png");
            Click(Button(w, confirm ? "OK" : "Cancel"));
            launcher.LaunchCount.ShouldBe(0);
            clipboard.Text.ShouldBe(confirm ? url : null);
        }
        finally
        {
            prompt?.Close();
            window.Close();
        }
    });

    [Fact]
    public Task Discarded_feed_url_opens_trusted_fallback_from_rendered_settings() => RunOnUiThread(() =>
    {
        var service = new UpdateService(_ => Task.FromResult("[{\"tag_name\":\"v999.0.0\",\"html_url\":\"file:///tmp/update\"}]"));
        var launcher = new FakeLauncherService();
        using var vm = new SettingsDocumentViewModel(new InMemorySettingsStore(), new FakeThemeService(),
            new FakeDialogService(), updates: service, launcher: launcher);
        var window = new Window { Content = new SettingsDocumentView { DataContext = vm }, Width = 900, Height = 800 };
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            Click(Button(window, "Check for updates"));
            launcher.LaunchCount.ShouldBe(0);
            Click(window.GetVisualDescendants().OfType<Button>().Single(b => ReferenceEquals(b.Command, vm.OpenLatestReleaseCommand)));
            launcher.LastUri.ShouldBe(service.ReleasesUrl(vm.UpdateChannel));
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
        if (Environment.GetEnvironmentVariable("GRPCN_UPDATE_SCREENSHOT_DIR") is not { Length: > 0 } directory) { return; }
        _ = Directory.CreateDirectory(directory);
        window.UpdateLayout();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
        using var bitmap = window.CaptureRenderedFrame().ShouldNotBeNull();
        bitmap.Save(Path.Combine(directory, name));
    }
}
