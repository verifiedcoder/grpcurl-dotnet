using GrpCurl.Net.Studio.TestSupport;
using GrpCurl.Net.Studio.ViewModels.Documents;
using GrpCurl.Net.Studio.ViewModels.Models.Connections;
using GrpCurl.Net.Studio.ViewModels.Models.Descriptors;
using GrpCurl.Net.Studio.ViewModels.Models.Invocation;

namespace GrpCurl.Net.Studio.Tests.Unit;

public sealed class InvocationDocumentViewModelTests
{
    private static SavedConnection Conn() => new() { Name = "c", Address = "h:1" };

    private static InvocationDocumentViewModel Create(
        out FakeInvocationRunner runner,
        out FakeDescriptorService descriptors,
        out FakeClipboardService clipboard,
        string? initialJson = "{}")
        => Create(out runner, out descriptors, out clipboard, out _, out _, out _, initialJson);

    private static InvocationDocumentViewModel Create(
        out FakeInvocationRunner runner,
        out FakeDescriptorService descriptors,
        out FakeClipboardService clipboard,
        out FakeDialogService dialogs,
        out FakeLauncherService launcher,
        out FakeRequestValidator validator,
        string? initialJson = "{}")
    {
        runner = new FakeInvocationRunner();
        descriptors = new FakeDescriptorService();
        clipboard = new FakeClipboardService();
        dialogs = new FakeDialogService();
        launcher = new FakeLauncherService();
        validator = new FakeRequestValidator();
        return new InvocationDocumentViewModel(
            Conn(), "pkg.Svc/Go", initialJson, runner, descriptors, new ImmediateUiDispatcher(), clipboard, dialogs, launcher, validator);
    }

    private static ErrorModel SampleError(int code = 5, string name = "NotFound", string headline = "missing") => new(
        ErrorCategoryKind.Rpc, code, name, StatusSeverityMap.FromCode(code), headline,
        Hint: null, Address: "h:1", Method: "pkg.Svc/Go",
        Suggestions: [new SuggestionModel("Check the method name.")],
        Details: [new HelpDetail([new HelpLink("Docs", "https://example.com/help")])],
        JsonEnvelope: "{\"kind\":\"error\"}");

    private static InvocationResultModel ErrorResult(ErrorModel error) => new(
        Ok: false, ResponseJson: null, ResponseHeaders: [], ResponseTrailers: [],
        Status: new InvocationStatusModel(error.StatusCode, error.StatusName, error.Headline),
        Timing: new TimingModel([], 0, 0), ErrorMessage: error.Headline, Error: error);

    private static InvocationResultModel OkResult() => new(
        Ok: true, ResponseJson: "{ \"ok\": true }",
        ResponseHeaders: [new MetadataItem("h", "1", false)],
        ResponseTrailers: [new MetadataItem("t", "2", false)],
        Status: new InvocationStatusModel(0, "OK", string.Empty),
        Timing: new TimingModel([new TimingPhase("Call", TimeSpan.FromMilliseconds(5))], 10, 20),
        ErrorMessage: null);

    [Fact]
    public void Initial_request_json_seeds_the_editor_and_title()
    {
        var doc = Create(out _, out _, out _, "{ \"x\": 1 }");

        doc.RequestJson.ShouldBe("{ \"x\": 1 }");
        doc.Title.ShouldBe("Go");
        doc.State.ShouldBe(RunState.Idle);
    }

    [Fact]
    public void Without_initial_json_the_template_is_fetched()
    {
        var descriptors = new FakeDescriptorService
        {
            OnDescribe = (_, symbol, _) => Task.FromResult(DescribeResult.Success(
                new MethodDescription(symbol, "Go", "f.proto", StreamingShape.Unary,
                    new TypeRef("pkg.In", true), new TypeRef("pkg.Out", true), new TypeRef("pkg.Svc", true), "{\n  \"seeded\": true\n}")))
        };

        var doc = new InvocationDocumentViewModel(
            Conn(), "pkg.Svc/Go", initialRequestJson: null, new FakeInvocationRunner(), descriptors, new ImmediateUiDispatcher(),
            new FakeClipboardService(), new FakeDialogService(), new FakeLauncherService(), new FakeRequestValidator());

        doc.RequestJson.ShouldContain("seeded");
    }

    [Fact]
    public async Task Invoke_populates_response_metadata_and_status_on_success()
    {
        var doc = Create(out var runner, out _, out _);
        runner.Result = OkResult();

        await doc.InvokeCommand.ExecuteAsync(null);

        doc.State.ShouldBe(RunState.Completed);
        doc.ResponseJson.ShouldBe("{ \"ok\": true }");
        doc.HasResponse.ShouldBeTrue();
        doc.ResponseHeaders.ShouldHaveSingleItem().Name.ShouldBe("h");
        doc.ResponseTrailers.ShouldHaveSingleItem().Name.ShouldBe("t");
        doc.Timing.ShouldHaveSingleItem().Phase.ShouldBe("Call");
        doc.StatusText.ShouldBe("OK");
        doc.StatusIsError.ShouldBeFalse();
    }

    [Fact]
    public async Task Invoke_failure_sets_the_failed_state_with_status()
    {
        var doc = Create(out var runner, out _, out _);
        runner.Result = ErrorResult(SampleError());

        await doc.InvokeCommand.ExecuteAsync(null);

        doc.State.ShouldBe(RunState.Failed);
        doc.StatusIsError.ShouldBeTrue();
        doc.StatusText.ShouldBe("NotFound");            // FR-091: pill shows the status name only
        doc.Severity.ShouldBe(StatusSeverity.Caller);
    }

    [Fact]
    public async Task Invoke_failure_exposes_the_rich_error_model()
    {
        var doc = Create(out var runner, out _, out _);
        runner.Result = ErrorResult(SampleError());

        await doc.InvokeCommand.ExecuteAsync(null);

        doc.HasError.ShouldBeTrue();
        doc.Error.ShouldNotBeNull();
        doc.Error!.Headline.ShouldBe("missing");
        doc.HasErrorSuggestions.ShouldBeTrue();
        doc.HasErrorDetails.ShouldBeTrue();
        doc.RetryCommand.CanExecute(null).ShouldBeTrue();
        doc.CopyErrorJsonCommand.CanExecute(null).ShouldBeTrue();
    }

    [Fact]
    public async Task A_successful_invoke_clears_a_previous_error()
    {
        var doc = Create(out var runner, out _, out _);
        runner.Result = ErrorResult(SampleError());
        await doc.InvokeCommand.ExecuteAsync(null);
        doc.HasError.ShouldBeTrue();

        runner.Result = OkResult();
        await doc.InvokeCommand.ExecuteAsync(null);

        doc.HasError.ShouldBeFalse();
        doc.Severity.ShouldBe(StatusSeverity.Ok);
    }

    [Fact]
    public async Task Retry_reinvokes_the_call()
    {
        var doc = Create(out var runner, out _, out _);
        runner.Result = ErrorResult(SampleError());
        await doc.InvokeCommand.ExecuteAsync(null);

        await doc.RetryCommand.ExecuteAsync(null);

        runner.InvokeCount.ShouldBe(2);
    }

    [Fact]
    public async Task Copy_error_json_writes_the_envelope()
    {
        var doc = Create(out var runner, out _, out var clipboard);
        runner.Result = ErrorResult(SampleError());
        await doc.InvokeCommand.ExecuteAsync(null);

        await doc.CopyErrorJsonCommand.ExecuteAsync(null);

        clipboard.Text.ShouldBe("{\"kind\":\"error\"}");
    }

    [Fact]
    public async Task Open_help_link_confirms_then_launches()
    {
        var doc = Create(out _, out _, out _, out var dialogs, out var launcher, out _);
        dialogs.ConfirmResult = true;

        await doc.OpenHelpLinkCommand.ExecuteAsync("https://example.com/help");

        dialogs.ConfirmCount.ShouldBe(1);
        launcher.LaunchCount.ShouldBe(1);
        launcher.LastUri.ShouldBe("https://example.com/help");
    }

    [Fact]
    public async Task Open_help_link_does_not_launch_when_declined()
    {
        var doc = Create(out _, out _, out _, out var dialogs, out var launcher, out _);
        dialogs.ConfirmResult = false;

        await doc.OpenHelpLinkCommand.ExecuteAsync("https://example.com/help");

        dialogs.ConfirmCount.ShouldBe(1);
        launcher.LaunchCount.ShouldBe(0);
    }

    [Fact]
    public async Task Cancellation_during_invoke_sets_the_cancelled_state()
    {
        var doc = Create(out var runner, out _, out _);
        runner.OnInvoke = (_, _) => throw new OperationCanceledException();

        await doc.InvokeCommand.ExecuteAsync(null);

        doc.State.ShouldBe(RunState.Cancelled);
        doc.StatusText.ShouldBe("Cancelled");
    }

    [Fact]
    public async Task Invoke_sends_the_current_request_and_options()
    {
        var doc = Create(out var runner, out _, out _);
        doc.RequestJson = "{ \"a\": 1 }";
        doc.Deadline = "10s";
        doc.EmitDefaults = true;
        doc.AddHeaderCommand.Execute(null);
        doc.Headers[0].Name = "x-test";
        doc.Headers[0].Value = "v";

        await doc.InvokeCommand.ExecuteAsync(null);

        var sent = runner.LastRequest.ShouldNotBeNull();
        sent.MethodSymbol.ShouldBe("pkg.Svc/Go");
        sent.RequestJson.ShouldBe("{ \"a\": 1 }");
        sent.Deadline.ShouldBe("10s");
        sent.EmitDefaults.ShouldBeTrue();
        sent.Headers.ShouldHaveSingleItem().Name.ShouldBe("x-test");
    }

    [Fact]
    public async Task Copy_response_writes_the_response_json()
    {
        var doc = Create(out var runner, out _, out var clipboard);
        runner.Result = OkResult();
        await doc.InvokeCommand.ExecuteAsync(null);

        await doc.CopyResponseCommand.ExecuteAsync(null);

        clipboard.Text.ShouldBe("{ \"ok\": true }");
    }

    [Fact]
    public async Task Copy_as_cli_writes_a_grpcn_invoke_command()
    {
        var doc = Create(out _, out _, out var clipboard);
        doc.Deadline = "5s";

        await doc.CopyAsCliCommand.ExecuteAsync(null);

        clipboard.Text.ShouldNotBeNull();
        clipboard.Text!.ShouldStartWith("grpcn invoke");
        clipboard.Text.ShouldContain("pkg.Svc/Go");
        clipboard.Text.ShouldContain("--max-time 5s");
    }

    [Fact]
    public void Add_and_remove_header_mutate_the_grid()
    {
        var doc = Create(out _, out _, out _);

        doc.AddHeaderCommand.Execute(null);
        doc.Headers.Count.ShouldBe(1);

        doc.RemoveHeaderCommand.Execute(doc.Headers[0]);
        doc.Headers.ShouldBeEmpty();
    }

    [Fact]
    public async Task Validation_surfaces_problems_from_the_validator()
    {
        var doc = Create(out _, out _, out _, out _, out _, out var validator);
        validator.Problems = [new ValidationProblem("Unexpected end of input", 2, 5)];

        await doc.RunValidationAsync(TestContext.Current.CancellationToken);

        doc.HasProblems.ShouldBeTrue();
        doc.Problems.ShouldHaveSingleItem().Display.ShouldBe("Unexpected end of input (line 2)");
    }

    [Fact]
    public async Task Validation_clears_problems_when_the_body_becomes_valid()
    {
        var doc = Create(out _, out _, out _, out _, out _, out var validator);
        validator.Problems = [new ValidationProblem("bad", 1, 1)];
        await doc.RunValidationAsync(TestContext.Current.CancellationToken);
        doc.HasProblems.ShouldBeTrue();

        validator.Problems = [];
        await doc.RunValidationAsync(TestContext.Current.CancellationToken);

        doc.HasProblems.ShouldBeFalse();
    }

    [Fact]
    public async Task Problems_never_block_invoke()
    {
        var doc = Create(out var runner, out _, out _, out _, out _, out var validator);
        validator.Problems = [new ValidationProblem("bad", 1, 1)];
        await doc.RunValidationAsync(TestContext.Current.CancellationToken);
        runner.Result = OkResult();

        doc.InvokeCommand.CanExecute(null).ShouldBeTrue();
        await doc.InvokeCommand.ExecuteAsync(null);

        doc.State.ShouldBe(RunState.Completed);
    }
}
