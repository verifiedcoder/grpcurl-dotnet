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
        new FakeClipboardService(),
        new FakeDialogService(),
        new FakeLauncherService(),
        new FakeRequestValidator());

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

    [Fact]
    public Task A_failed_invoke_renders_the_error_panel_with_pill_headline_and_a_rich_detail() => RunOnUiThread(() =>
    {
        var error = new ViewModels.Models.Invocation.ErrorModel(
            ViewModels.Models.Invocation.ErrorCategoryKind.Rpc, 9, "FailedPrecondition",
            ViewModels.Models.Invocation.StatusSeverity.Caller, "service is disabled",
            Hint: null, Address: "h:1", Method: "pkg.Svc/Go",
            Suggestions: [new ViewModels.Models.Invocation.SuggestionModel("Enable the service first.")],
            Details:
            [
                new ViewModels.Models.Invocation.BadRequestDetail(
                    [new ViewModels.Models.Invocation.FieldViolation("name", "must not be empty")])
            ],
            JsonEnvelope: "{\"kind\":\"error\"}");

        var runner = new FakeInvocationRunner
        {
            Result = new ViewModels.Models.Invocation.InvocationResultModel(
                false, null, [], [],
                new ViewModels.Models.Invocation.InvocationStatusModel(9, "FailedPrecondition", "service is disabled"),
                new ViewModels.Models.Invocation.TimingModel([], 0, 0), "service is disabled", error)
        };

        var vm = Vm(runner);
        var view = new InvocationDocumentView { DataContext = vm };
        var window = new Window { Content = view, Width = 800, Height = 600 };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        vm.InvokeCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();

        vm.State.ShouldBe(RunState.Failed);

        var texts = window.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).ToList();
        texts.ShouldContain("FailedPrecondition");     // status pill
        texts.ShouldContain("service is disabled");    // headline
        texts.ShouldContain("Bad request");            // rich-detail panel title rendered
        texts.ShouldContain("Try:");                   // suggestions section
    });

    private static InvocationDocumentViewModel ValidatingVm(FakeRequestValidator validator) => new(
        new SavedConnection { Name = "c", Address = "h:1" },
        "pkg.Svc/Go",
        "{\n  \"x\": 1\n}",
        new FakeInvocationRunner(),
        new FakeDescriptorService(),
        new ImmediateUiDispatcher(),
        new FakeClipboardService(),
        new FakeDialogService(),
        new FakeLauncherService(),
        validator);

    [Fact]
    public Task A_validation_problem_renders_in_the_problems_strip() => RunOnUiThread(() =>
    {
        var validator = new FakeRequestValidator
        {
            Problems = [new ViewModels.Models.Invocation.ValidationProblem("Unexpected character", 1, 3)]
        };

        var vm = ValidatingVm(validator);
        var view = new InvocationDocumentView { DataContext = vm };
        var window = new Window { Content = view, Width = 800, Height = 600 };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        vm.RunValidationAsync().GetAwaiter().GetResult();
        Dispatcher.UIThread.RunJobs();

        vm.HasProblems.ShouldBeTrue();
        window.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text)
            .ShouldContain("Unexpected character (line 1)");
    });
}
