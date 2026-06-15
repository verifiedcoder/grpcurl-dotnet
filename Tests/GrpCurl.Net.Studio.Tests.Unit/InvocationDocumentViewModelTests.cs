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
    {
        runner = new FakeInvocationRunner();
        descriptors = new FakeDescriptorService();
        clipboard = new FakeClipboardService();
        return new InvocationDocumentViewModel(
            Conn(), "pkg.Svc/Go", initialJson, runner, descriptors, new ImmediateUiDispatcher(), clipboard);
    }

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
            Conn(), "pkg.Svc/Go", initialRequestJson: null, new FakeInvocationRunner(), descriptors, new ImmediateUiDispatcher(), new FakeClipboardService());

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
        runner.Result = new InvocationResultModel(
            false, null, [], [], new InvocationStatusModel(5, "NotFound", "missing"),
            new TimingModel([], 0, 0), "missing");

        await doc.InvokeCommand.ExecuteAsync(null);

        doc.State.ShouldBe(RunState.Failed);
        doc.StatusIsError.ShouldBeTrue();
        doc.StatusText.ShouldBe("NotFound: missing");
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
    public void Add_and_remove_header_mutate_the_grid()
    {
        var doc = Create(out _, out _, out _);

        doc.AddHeaderCommand.Execute(null);
        doc.Headers.Count.ShouldBe(1);

        doc.RemoveHeaderCommand.Execute(doc.Headers[0]);
        doc.Headers.ShouldBeEmpty();
    }
}
