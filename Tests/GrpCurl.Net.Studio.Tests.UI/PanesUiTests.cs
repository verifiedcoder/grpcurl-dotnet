using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using GrpCurl.Net.Studio.Tests.UI.Headless;
using GrpCurl.Net.Studio.ViewModels.Panes;
using GrpCurl.Net.Studio.Views.Panes;

namespace GrpCurl.Net.Studio.Tests.UI;

/// <summary>L3 headless render for the CU-3 panes: the inspector content templates and console call rows.</summary>
public sealed class PanesUiTests(HeadlessSessionFixture fixture) : HeadlessTestBase(fixture)
{
    [Fact]
    public Task Inspector_renders_a_method_signature() => RunOnUiThread(() =>
    {
        var inspector = new InspectorViewModel();
        inspector.ShowMethod(new MethodSignatureContent("pkg.Svc/Go", "Go", "Unary", "pkg.In", "pkg.Out"));

        var window = new Window { Content = new InspectorView { DataContext = inspector }, Width = 320, Height = 480 };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var texts = window.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).ToList();
        texts.ShouldContain("pkg.Svc/Go");
        texts.ShouldContain("pkg.In");
        texts.ShouldContain("pkg.Out");
    });

    [Fact]
    public Task Console_renders_call_rows_and_selecting_one_feeds_the_inspector() => RunOnUiThread(() =>
    {
        var inspector = new InspectorViewModel();
        var console = new ConsoleViewModel(inspector);
        console.AppendCall(new ConsoleCallActivity(
            "pkg.Svc/Go", 0, "OK", IsError: false, "12 ms",
            [new CallTimingPhase("call", "12 ms", 1.0)]));

        var window = new Window { Content = new ConsoleView { DataContext = console }, Width = 480, Height = 200 };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var list = window.GetVisualDescendants().OfType<ListBox>().First();
        window.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).ShouldContain("pkg.Svc/Go");

        // Selecting the row routes its breakdown to the inspector (FR-114).
        list.SelectedItem = console.Calls[0];
        Dispatcher.UIThread.RunJobs();
        inspector.Content.ShouldBeOfType<CallTimingContent>();
    });
}
