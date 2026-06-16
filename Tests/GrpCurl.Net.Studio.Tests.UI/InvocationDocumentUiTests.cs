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
    public Task A_successful_invoke_renders_the_save_response_button() => RunOnUiThread(() =>
    {
        // FR-074: a multi-line body exercises the brace-folding rebuild in SetResponseText and
        // the Save… button materialises once a response exists.
        var runner = new FakeInvocationRunner
        {
            Result = new ViewModels.Models.Invocation.InvocationResultModel(
                true, "{\n  \"echo\": 42,\n  \"nested\": {\n    \"k\": \"v\"\n  }\n}", [], [],
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

        vm.HasResponse.ShouldBeTrue();
        window.GetVisualDescendants().OfType<Button>().Select(b => b.Content as string)
            .ShouldContain("Save…");
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

        // Populate the failed state BEFORE the window lays out so the error panel's nested item
        // bindings attach during the initial layout pass (deterministic across headless OSes).
        var vm = Vm(runner);
        vm.InvokeCommand.Execute(null);
        vm.State.ShouldBe(RunState.Failed);

        var view = new InvocationDocumentView { DataContext = vm };
        var window = new Window { Content = view, Width = 800, Height = 600 };
        window.Show();
        Dispatcher.UIThread.RunJobs();

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

        // Populate the problems BEFORE layout so the strip binds during the initial pass.
        var vm = ValidatingVm(validator);
        vm.RunValidationAsync().GetAwaiter().GetResult();
        vm.HasProblems.ShouldBeTrue();

        var view = new InvocationDocumentView { DataContext = vm };
        var window = new Window { Content = view, Width = 800, Height = 600 };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        window.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text)
            .ShouldContain("Unexpected character (line 1)");
    });

    private static InvocationDocumentViewModel StreamingVm(GrpCurl.Net.Studio.ViewModels.Models.Descriptors.StreamingShape shape, FakeInvocationRunner runner)
    {
        var descriptors = new FakeDescriptorService
        {
            OnDescribe = (_, symbol, _) => Task.FromResult(GrpCurl.Net.Studio.ViewModels.Models.Descriptors.DescribeResult.Success(
                new GrpCurl.Net.Studio.ViewModels.Models.Descriptors.MethodDescription(symbol, "Go", "f.proto", shape,
                    new GrpCurl.Net.Studio.ViewModels.Models.Descriptors.TypeRef("pkg.In", true),
                    new GrpCurl.Net.Studio.ViewModels.Models.Descriptors.TypeRef("pkg.Out", true),
                    new GrpCurl.Net.Studio.ViewModels.Models.Descriptors.TypeRef("pkg.Svc", true), "{}")))
        };

        return new InvocationDocumentViewModel(
            new SavedConnection { Name = "c", Address = "h:1" }, "pkg.Svc/Go", "{}", runner, descriptors,
            new ImmediateUiDispatcher(), new FakeClipboardService(), new FakeDialogService(), new FakeLauncherService(),
            new FakeRequestValidator());
    }

    [Fact]
    public Task A_server_streaming_tab_renders_the_event_log_with_rows() => RunOnUiThread(() =>
    {
        var vm = StreamingVm(GrpCurl.Net.Studio.ViewModels.Models.Descriptors.StreamingShape.ServerStreaming, new FakeInvocationRunner());
        vm.IsStreaming.ShouldBeTrue();

        // Populate the log directly (the async StartStream pipeline is L1-tested); render the layout.
        vm.Log.Append(new ViewModels.Models.Invocation.StreamEventModel(ViewModels.Models.Invocation.StreamEventKind.Headers, -1, DateTimeOffset.Now, 0, "headers"));
        vm.Log.Append(new ViewModels.Models.Invocation.StreamEventModel(ViewModels.Models.Invocation.StreamEventKind.MessageReceived, 0, DateTimeOffset.Now, 0, "echo 1"));

        var window = new Window { Content = new InvocationDocumentView { DataContext = vm }, Width = 800, Height = 600 };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        window.GetVisualDescendants().OfType<ListBox>()
            .Any(l => Equals(l.GetValue(Avalonia.Automation.AutomationProperties.NameProperty), "Event log"))
            .ShouldBeTrue();
        window.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).ShouldContain("echo 1");
    });
}
