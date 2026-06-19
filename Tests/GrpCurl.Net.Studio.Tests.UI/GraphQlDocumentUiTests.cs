using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using GrpCurl.Net.Studio.Tests.UI.Headless;
using GrpCurl.Net.Studio.TestSupport;
using GrpCurl.Net.Studio.ViewModels.Documents;
using GrpCurl.Net.Studio.ViewModels.Models.Connections;
using GrpCurl.Net.Studio.ViewModels.Models.GraphQl;
using GrpCurl.Net.Studio.Views.Documents;

namespace GrpCurl.Net.Studio.Tests.UI;

/// <summary>
///     L3 headless render of the GraphQL operation tab (SPEC-015 E4.1): the real view binds to a parsed
///     document and renders the connection target, the operation picker, and the Execute action without
///     throwing.
/// </summary>
public sealed class GraphQlDocumentUiTests(HeadlessSessionFixture fixture) : HeadlessTestBase(fixture)
{
    private static GraphQlDocumentViewModel Document()
    {
        var vm = new GraphQlDocumentViewModel(
            new SavedConnection { Name = "c", Address = "h:1" },
            new FakeGraphQlService(),
            new ImmediateUiDispatcher(),
            new FakeClipboardService())
        {
            ParseDebounce = TimeSpan.Zero
        };

        vm.ApplyParse(new GraphQlParseResult([new GraphQlOperationInfo("Q", GraphQlOperationKind.Query)], []));
        return vm;
    }

    [Fact]
    public Task GraphQl_tab_renders_target_picker_and_execute() => RunOnUiThread(() =>
    {
        var doc = Document();

        var window = new Window { Content = new GraphQlDocumentView { DataContext = doc }, Width = 800, Height = 600 };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var texts = window.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).ToList();
        texts.ShouldContain("h:1"); // connection address in the header bar

        var buttons = window.GetVisualDescendants().OfType<Button>().ToList();
        buttons.ShouldContain(b => Equals(b.Content, "Execute"));

        // The operation picker is populated from the parse.
        var picker = window.GetVisualDescendants().OfType<ComboBox>().First();
        picker.ItemCount.ShouldBe(1);
    });
}
