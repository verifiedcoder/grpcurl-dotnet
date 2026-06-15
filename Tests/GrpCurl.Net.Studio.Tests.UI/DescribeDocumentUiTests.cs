using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using GrpCurl.Net.Studio.TestSupport;
using GrpCurl.Net.Studio.Tests.UI.Headless;
using GrpCurl.Net.Studio.ViewModels.Documents;
using GrpCurl.Net.Studio.ViewModels.Models.Connections;
using GrpCurl.Net.Studio.ViewModels.Models.Descriptors;
using GrpCurl.Net.Studio.Views.Documents;

namespace GrpCurl.Net.Studio.Tests.UI;

/// <summary>
///     L3 headless E2E for the describe tab (FR-050/051/052): renders the real view bound to a
///     loaded message and asserts the field table, the request template, and type-link navigation.
/// </summary>
public sealed class DescribeDocumentUiTests(HeadlessSessionFixture fixture) : HeadlessTestBase(fixture)
{
    private static DescribeDocumentViewModel LoadedMessageDocument()
    {
        var descriptors = new FakeDescriptorService
        {
            OnDescribe = (_, symbol, _) => Task.FromResult(DescribeResult.Success(
                symbol == "pkg.Alpha"
                    ? new MessageDescription("pkg.Alpha", "Alpha", "a.proto",
                        [new FieldDescription("beta", 1, ".pkg.Beta", new TypeRef("pkg.Beta", Resolvable: true), FieldLabel.Optional, null)],
                        [], "{\n  \"beta\": {}\n}")
                    : new MessageDescription(symbol, symbol, "b.proto", [], [], "{}")))
        };

        return new DescribeDocumentViewModel(
            new SavedConnection { Name = "c", Address = "h:1" },
            "pkg.Alpha", descriptors, new ImmediateUiDispatcher(), new FakeClipboardService(), new FakeDocumentHost());
    }

    [Fact]
    public Task Describe_tab_renders_the_field_table_and_template() => RunOnUiThread(() =>
    {
        var doc = LoadedMessageDocument();
        doc.IsLoaded.ShouldBeTrue();

        var window = new Window { Content = new DescribeDocumentView { DataContext = doc }, Width = 600, Height = 400 };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var texts = window.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).ToList();
        texts.ShouldContain("pkg.Alpha"); // header
        texts.ShouldContain(" beta");      // field name (StringFormat ' {0}')

        // The request template renders in the read-only editor box.
        var template = window.GetVisualDescendants().OfType<TextBox>().Single();
        template.Text.ShouldNotBeNull();
        template.Text!.ShouldContain("beta");
    });

    [Fact]
    public Task Clicking_a_type_link_navigates_in_tab() => RunOnUiThread(() =>
    {
        var doc = LoadedMessageDocument();

        var window = new Window { Content = new DescribeDocumentView { DataContext = doc }, Width = 600, Height = 400 };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var link = window.GetVisualDescendants().OfType<Button>().First(b => b.Classes.Contains("link"));
        link.Command.ShouldNotBeNull();
        link.Command!.Execute(link.CommandParameter);
        Dispatcher.UIThread.RunJobs();

        doc.CurrentSymbol.ShouldBe("pkg.Beta");
    });
}
