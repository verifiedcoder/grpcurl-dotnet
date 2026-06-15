using Avalonia;
using Avalonia.Media;
using GrpCurl.Net.Studio.Services;
using GrpCurl.Net.Studio.Tests.UI.Headless;
using GrpCurl.Net.Studio.Theming;

namespace GrpCurl.Net.Studio.Tests.UI;

/// <summary>L3-ish: the editor-options manager pushes FR-152 settings into live application resources.</summary>
public sealed class EditorOptionsManagerTests(HeadlessSessionFixture fixture) : HeadlessTestBase(fixture)
{
    [Fact]
    public Task Attach_applies_font_and_indent_then_refreshes_on_change() => RunOnUiThread(() =>
    {
        var app = Application.Current!;
        var store = new InMemorySettingsStore();
        store.Current.Editor.FontFamily = "Consolas";
        store.Current.Editor.FontSize = 17;
        store.Current.Editor.IndentWidth = 4;

        new EditorOptionsManager(app, store).Attach();

        app.Resources["Editor.FontSize"].ShouldBe(17d);
        app.Resources["Editor.IndentationSize"].ShouldBe(4);
        ((FontFamily)app.Resources["Editor.FontFamily"]!).ToString().ShouldContain("Consolas");

        // Persisting new settings re-applies live.
        var updated = store.Current;
        updated.Editor.FontSize = 21;
        store.SaveAsync(updated).GetAwaiter().GetResult();

        app.Resources["Editor.FontSize"].ShouldBe(21d);
    });
}
