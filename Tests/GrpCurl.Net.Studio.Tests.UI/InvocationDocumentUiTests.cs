using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AvaloniaEdit;
using GrpCurl.Net.Studio.TestSupport;
using GrpCurl.Net.Studio.Tests.UI.Headless;
using GrpCurl.Net.Studio.ViewModels.Documents;
using GrpCurl.Net.Studio.ViewModels.Models.Connections;
using GrpCurl.Net.Studio.Views.Documents;

namespace GrpCurl.Net.Studio.Tests.UI;

/// <summary>
///     L3 headless E2E for the invocation tab (FR-060..): renders the real view (AvaloniaEdit +
///     TextMate JSON grammar) bound to a VM and asserts the editor, method binding, and an invoke
///     result all materialize headlessly on every CI OS.
/// </summary>
public sealed class InvocationDocumentUiTests(HeadlessSessionFixture fixture) : HeadlessTestBase(fixture)
{
    private static InvocationDocumentViewModel Vm(FakeInvocationRunner runner) => new(
        new SavedConnection { Name = "c", Address = "h:1" },
        "pkg.Svc/Go",
        "{\n  \"x\": 1\n}",
        runner,
        new FakeDescriptorService(),
        new ImmediateUiDispatcher(),
        new FakeClipboardService());

    [Fact]
    public Task Invocation_tab_renders_the_editor_and_method_binding() => RunOnUiThread(() =>
    {
        var view = new InvocationDocumentView { DataContext = Vm(new FakeInvocationRunner()) };
        var window = new Window { Content = view, Width = 800, Height = 600 };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        // Method binding renders, and the AvaloniaEdit request editor holds the seeded JSON.
        window.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).ShouldContain("pkg.Svc/Go");

        var editor = window.GetVisualDescendants().OfType<TextEditor>().First(e => e.Name == "RequestEditor");
        editor.Text.ShouldContain("\"x\"");
    });

    [Fact]
    public Task Invoke_renders_the_response_body() => RunOnUiThread(() =>
    {
        var runner = new FakeInvocationRunner
        {
            Result = new ViewModels.Models.Invocation.InvocationResultModel(
                true, "{ \"echo\": 42 }", [], [],
                new ViewModels.Models.Invocation.InvocationStatusModel(0, "OK", string.Empty),
                new ViewModels.Models.Invocation.TimingModel([], 0, 0), null)
        };

        var vm = Vm(runner);
        var view = new InvocationDocumentView { DataContext = vm };
        var window = new Window { Content = view, Width = 800, Height = 600 };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        vm.InvokeCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();

        vm.State.ShouldBe(RunState.Completed);

        var response = window.GetVisualDescendants().OfType<TextEditor>().First(e => e.Name == "ResponseEditor");
        response.Text.ShouldContain("echo");
    });
}
