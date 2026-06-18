using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using GrpCurl.Net.Studio.Services;
using GrpCurl.Net.Studio.TestSupport;
using GrpCurl.Net.Studio.Tests.UI.Headless;
using GrpCurl.Net.Studio.ViewModels.Documents;
using GrpCurl.Net.Studio.ViewModels.Models;
using GrpCurl.Net.Studio.ViewModels.Services;
using GrpCurl.Net.Studio.Views.Documents;

namespace GrpCurl.Net.Studio.Tests.UI;

/// <summary>L3 headless E2E for the Settings tab (FR-150..152): the categories render and edits persist.</summary>
public sealed class SettingsDocumentUiTests(HeadlessSessionFixture fixture) : HeadlessTestBase(fixture)
{
    [Fact]
    public Task Settings_tab_renders_general_and_editor_sections() => RunOnUiThread(() =>
    {
        var store = new InMemorySettingsStore();
        var vm = new SettingsDocumentViewModel(store, new FakeThemeService(), new FakeDialogService(), new FakeProtocService());

        var window = new Window { Content = new SettingsDocumentView { DataContext = vm }, Width = 700, Height = 600 };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var texts = window.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).ToList();
        texts.ShouldContain("General");
        texts.ShouldContain("Editor");
        texts.ShouldContain("Theme");
        texts.ShouldContain("Network defaults");
        texts.ShouldContain("protoc");
        texts.ShouldContain("History");          // FR-158: active section
        texts.ShouldContain("Descriptor limits"); // FR-157: active section
        texts.ShouldContain("Updates");          // FR-156: active section
        texts.ShouldContain("Diagnostics");      // FR-155: active section

        // FR-158: the history capture toggle renders as a real, named control.
        window.GetVisualDescendants().OfType<CheckBox>()
            .Any(c => Equals(c.GetValue(Avalonia.Automation.AutomationProperties.NameProperty), "Capture history"))
            .ShouldBeTrue();

        window.GetVisualDescendants().OfType<Button>()
            .Any(b => Equals(b.GetValue(Avalonia.Automation.AutomationProperties.NameProperty), "Reset all settings"))
            .ShouldBeTrue();
    });

    [Fact]
    public Task Diagnostics_viewer_renders_log_entries() => RunOnUiThread(() =>
    {
        var log = new FakeDiagnosticsLog();
        log.Entries.Add(new(System.DateTimeOffset.UtcNow,
            GrpCurl.Net.Studio.ViewModels.Models.Diagnostics.DiagnosticsLevel.Warning, "Net", "connect timeout marker"));
        var vm = new SettingsDocumentViewModel(
            new InMemorySettingsStore(), new FakeThemeService(), new FakeDialogService(), new FakeProtocService(),
            secrets: null, new FakeUpdateService(), new FakeLauncherService(), log, new FakeClipboardService());

        var window = new Window { Content = new SettingsDocumentView { DataContext = vm }, Width = 700, Height = 600 };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var texts = window.GetVisualDescendants().OfType<TextBlock>().SelectMany(Runs).ToList();
        texts.ShouldContain("connect timeout marker"); // the seeded entry renders

        window.GetVisualDescendants().OfType<Button>()
            .Any(b => Equals(b.GetValue(Avalonia.Automation.AutomationProperties.NameProperty), "Copy diagnostics bundle"))
            .ShouldBeTrue();
    });

    private static IEnumerable<string?> Runs(TextBlock block)
    {
        yield return block.Text;
        foreach (var inline in block.Inlines ?? [])
        {
            if (inline is Avalonia.Controls.Documents.Run run)
            {
                yield return run.Text;
            }
        }
    }

    [Fact]
    public Task Security_panel_shows_the_backend_and_fallback_limitation() => RunOnUiThread(() =>
    {
        var secrets = new FakeSecretStore
        {
            Info = new SecretStoreInfo("Encrypted file (fallback)", IsOsKeychain: false, LimitationNote: "no OS keychain was available")
        };
        var vm = new SettingsDocumentViewModel(
            new InMemorySettingsStore(), new FakeThemeService(), new FakeDialogService(), new FakeProtocService(), secrets);

        var window = new Window { Content = new SettingsDocumentView { DataContext = vm }, Width = 700, Height = 600 };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var texts = window.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).ToList();
        texts.ShouldContain("Security");
        texts.ShouldContain("Encrypted file (fallback)");
        texts.ShouldContain("no OS keychain was available"); // SEC-024 limitation rendered verbatim
    });

    [Fact]
    public Task Toggling_format_on_paste_in_the_view_persists() => RunOnUiThread(() =>
    {
        var store = new InMemorySettingsStore();
        var vm = new SettingsDocumentViewModel(store, new FakeThemeService(), new FakeDialogService(), new FakeProtocService());

        var window = new Window { Content = new SettingsDocumentView { DataContext = vm }, Width = 700, Height = 600 };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var checkbox = window.GetVisualDescendants().OfType<CheckBox>()
            .Single(c => Equals(c.GetValue(Avalonia.Automation.AutomationProperties.NameProperty), "Format on paste"));
        checkbox.IsChecked = false;
        Dispatcher.UIThread.RunJobs();

        store.Current.Editor.FormatOnPaste.ShouldBeFalse();
    });
}
