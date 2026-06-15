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
        var vm = new SettingsDocumentViewModel(store, new FakeThemeService());

        var window = new Window { Content = new SettingsDocumentView { DataContext = vm }, Width = 700, Height = 600 };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var texts = window.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).ToList();
        texts.ShouldContain("General");
        texts.ShouldContain("Editor");
        texts.ShouldContain("Theme");
    });

    [Fact]
    public Task Toggling_format_on_paste_in_the_view_persists() => RunOnUiThread(() =>
    {
        var store = new InMemorySettingsStore();
        var vm = new SettingsDocumentViewModel(store, new FakeThemeService());

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
