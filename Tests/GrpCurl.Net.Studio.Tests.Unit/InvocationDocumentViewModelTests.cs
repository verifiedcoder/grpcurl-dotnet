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

    private static InvocationDocumentViewModel CreateStreaming(StreamingShape shape, out FakeInvocationRunner runner)
    {
        var captured = new FakeInvocationRunner();
        runner = captured;
        var descriptors = new FakeDescriptorService
        {
            OnDescribe = (_, symbol, _) => Task.FromResult(DescribeResult.Success(
                new MethodDescription(symbol, "Go", "f.proto", shape,
                    new TypeRef("pkg.In", true), new TypeRef("pkg.Out", true), new TypeRef("pkg.Svc", true), "{}")))
        };

        return new InvocationDocumentViewModel(
            Conn(), "pkg.Svc/Go", initialRequestJson: "{}", captured, descriptors, new ImmediateUiDispatcher(),
            new FakeClipboardService(), new FakeDialogService(), new FakeLauncherService(), new FakeRequestValidator());
    }

    private static StreamEventModel Ev(StreamEventKind kind, long index = -1, InvocationStatusModel? status = null)
        => new(kind, index, DateTimeOffset.Now, 0, kind.ToString(), Status: status);

    private static InvocationDocumentViewModel CreateStreamingWithCapture(
        out FakeInvocationRunner runner, out FakeFilePickerService picker, out StringWriter sink, int ringCapacity = 10)
    {
        var captured = new FakeInvocationRunner();
        var captPicker = new FakeFilePickerService();
        var captSink = new StringWriter();
        runner = captured;
        picker = captPicker;
        sink = captSink;
        var descriptors = new FakeDescriptorService
        {
            OnDescribe = (_, symbol, _) => Task.FromResult(DescribeResult.Success(
                new MethodDescription(symbol, "Go", "f.proto", StreamingShape.ServerStreaming,
                    new TypeRef("pkg.In", true), new TypeRef("pkg.Out", true), new TypeRef("pkg.Svc", true), "{}")))
        };

        return new InvocationDocumentViewModel(
            Conn(), "pkg.Svc/Go", "{}", captured, descriptors, new ImmediateUiDispatcher(),
            new FakeClipboardService(), new FakeDialogService(), new FakeLauncherService(), new FakeRequestValidator(),
            captPicker, ringCapacity, _ => captSink);
    }

    private static StreamEventModel Msg(long index)
        => new(StreamEventKind.MessageReceived, index, DateTimeOffset.Now, 0, $"msg {index}",
            RawMessage: new Google.Protobuf.WellKnownTypes.Empty());

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
    public void An_invalid_bin_header_blocks_invoke()
    {
        var doc = Create(out _, out _, out _);
        doc.AddHeaderCommand.Execute(null);
        var row = doc.Headers[0];

        row.Name = "trace-bin";
        row.Value = "not base64!!";

        doc.HasHeaderErrors.ShouldBeTrue();
        doc.InvokeCommand.CanExecute(null).ShouldBeFalse();

        row.Value = "AAEC"; // fixed
        doc.HasHeaderErrors.ShouldBeFalse();
    }

    [Fact]
    public void Switching_body_format_with_content_warns_and_clears_when_confirmed()
    {
        var doc = Create(out _, out _, out _, out var dialogs, out _, out _, initialJson: "{ \"x\": 1 }");
        dialogs.ConfirmResult = true; // "clear it"

        doc.BodyFormat = RequestBodyFormat.Text;

        dialogs.ConfirmCount.ShouldBe(1);
        doc.RequestJson.ShouldBeEmpty();
    }

    [Fact]
    public void Switching_body_format_keeps_content_when_declined()
    {
        var doc = Create(out _, out _, out _, out var dialogs, out _, out _, initialJson: "value: 1");
        dialogs.ConfirmResult = false; // keep

        doc.BodyFormat = RequestBodyFormat.Text;

        dialogs.ConfirmCount.ShouldBe(1);
        doc.RequestJson.ShouldBe("value: 1");
    }

    [Fact]
    public async Task A_verbose_transcript_populates_the_raw_tab()
    {
        var doc = Create(out var runner, out _, out var clipboard);
        runner.Result = runner.Result with
        {
            Transcript = new VerboseTranscript(
                "localhost:443", "edge",
                RequestHeaders: [new MetadataItem("x-trace", "abc", false)],
                ResponseHeaders: [], ResponseTrailers: [],
                RequestMessages: 1, ResponseMessages: 1,
                Status: new InvocationStatusModel(0, "OK", string.Empty))
        };

        await doc.InvokeCommand.ExecuteAsync(null);

        doc.HasRawTranscript.ShouldBeTrue();
        doc.RawTranscript!.ShouldContain("localhost:443");
        doc.RawTranscript!.ShouldContain("x-trace: abc");

        await doc.CopyRawTranscriptCommand.ExecuteAsync(null);
        clipboard.Text.ShouldBe(doc.RawTranscript);
    }

    [Fact]
    public async Task Timing_rows_carry_each_phase_fraction_of_the_total()
    {
        var doc = Create(out var runner, out _, out _);
        runner.Result = runner.Result with
        {
            Timing = new TimingModel(
                [
                    new TimingPhase("descriptor", TimeSpan.FromMilliseconds(30)),
                    new TimingPhase("call", TimeSpan.FromMilliseconds(70)),
                    new TimingPhase("total", TimeSpan.FromMilliseconds(100))
                ],
                RequestBytes: 12, ResponseBytes: 34)
        };

        await doc.InvokeCommand.ExecuteAsync(null);

        doc.Timing.Count.ShouldBe(3);
        doc.Timing[0].Phase.ShouldBe("descriptor");
        doc.Timing[0].Fraction.ShouldBe(0.3, 0.001);
        doc.Timing[1].Fraction.ShouldBe(0.7, 0.001);
        doc.Timing[2].IsTotal.ShouldBeTrue();
        doc.Timing[2].Fraction.ShouldBe(1.0);
        doc.Timing[2].PercentText.ShouldBeEmpty();
        doc.TimingBytesText!.ShouldContain("12");
        doc.TimingBytesText!.ShouldContain("34");
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

    [Theory]
    [InlineData("file:///etc/passwd")]
    [InlineData("javascript:alert(1)")]
    [InlineData("calc://run")]
    [InlineData("not even a uri")]
    public async Task Open_help_link_never_launches_a_non_http_scheme(string url)
    {
        // The link is server-controlled (google.rpc.Help); only http/https may be launched.
        var doc = Create(out _, out _, out var clipboard, out var dialogs, out var launcher, out _);
        dialogs.ConfirmResult = true; // user accepts the "copy instead?" prompt

        await doc.OpenHelpLinkCommand.ExecuteAsync(url);

        launcher.LaunchCount.ShouldBe(0);     // never handed to the OS launcher
        clipboard.Text.ShouldBe(url);         // offered for copy instead
    }

    [Fact]
    public async Task Open_help_link_allows_http_scheme()
    {
        var doc = Create(out _, out _, out _, out var dialogs, out var launcher, out _);
        dialogs.ConfirmResult = true;

        await doc.OpenHelpLinkCommand.ExecuteAsync("http://example.com/help");

        launcher.LaunchCount.ShouldBe(1);
        launcher.LastUri.ShouldBe("http://example.com/help");
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

    [Fact]
    public void Server_streaming_shape_is_detected_and_invoke_is_disabled()
    {
        var doc = CreateStreaming(StreamingShape.ServerStreaming, out _);

        doc.Shape.ShouldBe(StreamingShape.ServerStreaming);
        doc.IsStreaming.ShouldBeTrue();
        doc.HasComposer.ShouldBeFalse();
        doc.InvokeCommand.CanExecute(null).ShouldBeFalse();
        doc.StartStreamCommand.CanExecute(null).ShouldBeTrue();
    }

    [Fact]
    public void Client_streaming_shape_creates_a_composer()
    {
        var doc = CreateStreaming(StreamingShape.ClientStreaming, out _);

        doc.HasComposer.ShouldBeTrue();
        doc.Composer.ShouldNotBeNull();
    }

    [Fact]
    public async Task Copy_as_cli_for_a_streaming_tab_emits_a_runnable_json_array()
    {
        var descriptors = new FakeDescriptorService
        {
            OnDescribe = (_, symbol, _) => Task.FromResult(DescribeResult.Success(
                new MethodDescription(symbol, "Go", "f.proto", StreamingShape.ClientStreaming,
                    new TypeRef("pkg.In", true), new TypeRef("pkg.Out", true), new TypeRef("pkg.Svc", true), "{}")))
        };
        var clipboard = new FakeClipboardService();
        var doc = new InvocationDocumentViewModel(
            Conn(), "pkg.Svc/Go", "{}", new FakeInvocationRunner(), descriptors, new ImmediateUiDispatcher(),
            clipboard, new FakeDialogService(), new FakeLauncherService(), new FakeRequestValidator());
        doc.HasComposer.ShouldBeTrue();
        doc.Composer!.MessageJson = "{ \"x\": 1 }";

        await doc.CopyAsCliCommand.ExecuteAsync(null);

        var command = clipboard.Text.ShouldNotBeNull();
        command.ShouldNotContain("\n");                  // a single runnable line, not a comment + loose -d
        command.ShouldContain("-d '[{ \"x\": 1 }]'");    // the client/bidi array grammar
    }

    [Fact]
    public async Task Start_stream_populates_the_event_log_and_final_status()
    {
        var doc = CreateStreaming(StreamingShape.ServerStreaming, out var runner);
        runner.StreamEvents =
        [
            Ev(StreamEventKind.Headers),
            Ev(StreamEventKind.MessageReceived, 0),
            Ev(StreamEventKind.MessageReceived, 1),
            Ev(StreamEventKind.Status, status: new InvocationStatusModel(0, "OK", string.Empty))
        ];

        await doc.StartStreamCommand.ExecuteAsync(null);

        doc.State.ShouldBe(RunState.Completed);
        doc.Log.TotalReceived.ShouldBe(2);
        doc.Log.Rows.Count.ShouldBe(4);
        doc.StatusText.ShouldBe("OK");
    }

    [Fact]
    public async Task Stream_cancellation_preserves_received_rows_and_records_a_cancel_row()
    {
        var doc = CreateStreaming(StreamingShape.ServerStreaming, out var runner);
        runner.OnStream = (_, _, _) => OneMessageThenCancel();

        await doc.StartStreamCommand.ExecuteAsync(null);

        doc.State.ShouldBe(RunState.Cancelled);
        doc.Log.TotalReceived.ShouldBe(1);                                  // preserved
        doc.Log.Rows[^1].Kind.ShouldBe(StreamEventKind.Status);             // final cancel row
        doc.Log.Rows[^1].Preview.ShouldContain("Cancelled");
    }

    private static async IAsyncEnumerable<StreamEventModel> OneMessageThenCancel()
    {
        yield return new StreamEventModel(StreamEventKind.MessageReceived, 0, DateTimeOffset.Now, 0, "msg");
        await Task.Yield();
        throw new OperationCanceledException();
    }

    [Fact]
    public async Task Export_stream_writes_the_retained_rows_as_ndjson()
    {
        var doc = CreateStreamingWithCapture(out var runner, out var picker, out var sink);
        runner.StreamEvents = [Msg(0), Msg(1), Ev(StreamEventKind.Status, status: new InvocationStatusModel(0, "OK", string.Empty))];
        await doc.StartStreamCommand.ExecuteAsync(null);
        picker.SaveResult = "/tmp/export.ndjson";

        await doc.ExportStreamCommand.ExecuteAsync(null);

        var lines = sink.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries);
        lines.Length.ShouldBe(3); // two messages + the status row
        lines[0].ShouldContain("\"kind\":\"message\"");
    }

    [Fact]
    public async Task Capture_writes_every_event_including_rows_evicted_from_the_ring()
    {
        var doc = CreateStreamingWithCapture(out var runner, out var picker, out var sink, ringCapacity: 2);
        picker.SaveResult = "/tmp/capture.ndjson";
        runner.StreamEvents = [Msg(0), Msg(1), Msg(2), Msg(3), Ev(StreamEventKind.Status, status: new InvocationStatusModel(0, "OK", string.Empty))];

        await doc.ToggleCaptureCommand.ExecuteAsync(null);
        doc.IsCapturing.ShouldBeTrue();

        await doc.StartStreamCommand.ExecuteAsync(null);

        doc.Log.Rows.Count.ShouldBe(2);            // ring kept only the last 2
        doc.Log.TotalReceived.ShouldBe(4);
        var lines = sink.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries);
        lines.Length.ShouldBe(5);                  // capture lost nothing (4 messages + status)
        doc.CaptureBytes.ShouldBeGreaterThan(0);
    }

    [Fact]
    public async Task Toggling_capture_off_stops_capturing()
    {
        var doc = CreateStreamingWithCapture(out _, out var picker, out _);
        picker.SaveResult = "/tmp/c.ndjson";

        await doc.ToggleCaptureCommand.ExecuteAsync(null);
        doc.IsCapturing.ShouldBeTrue();

        await doc.ToggleCaptureCommand.ExecuteAsync(null);
        doc.IsCapturing.ShouldBeFalse();
    }

    // ── CU-2 FR-073: live elapsed + deadline countdown ───────────────────────

    private static InvocationDocumentViewModel CreateWithCapture(
        out FakeInvocationRunner runner, out FakeFilePickerService picker, out StringWriter sink, out FakeDocumentHost host)
    {
        var captRunner = new FakeInvocationRunner();
        var captPicker = new FakeFilePickerService();
        var captSink = new StringWriter();
        var captHost = new FakeDocumentHost();
        runner = captRunner;
        picker = captPicker;
        sink = captSink;
        host = captHost;
        return new InvocationDocumentViewModel(
            Conn(), "pkg.Svc/Go", "{}", captRunner, new FakeDescriptorService(), new ImmediateUiDispatcher(),
            new FakeClipboardService(), new FakeDialogService(), new FakeLauncherService(), new FakeRequestValidator(),
            captPicker, 10, _ => captSink, revealGate: null, documentHost: captHost);
    }

    [Fact]
    public void Begin_elapsed_with_a_deadline_reports_elapsed_and_countdown()
    {
        var doc = Create(out _, out _, out _);
        var start = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        doc.BeginElapsed(start, start.AddSeconds(10));
        doc.UpdateElapsed(start.AddSeconds(3));

        doc.ElapsedText.ShouldBe("3.0s elapsed");
        doc.HasDeadlineRemaining.ShouldBeTrue();
        doc.DeadlineRemainingText.ShouldBe("7.0s to deadline");
    }

    [Fact]
    public void Elapsed_countdown_clamps_to_zero_past_the_deadline()
    {
        var doc = Create(out _, out _, out _);
        var start = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        doc.BeginElapsed(start, start.AddSeconds(5));
        doc.UpdateElapsed(start.AddSeconds(8));

        doc.DeadlineRemainingText.ShouldBe("0.0s to deadline");
    }

    [Fact]
    public void Begin_elapsed_without_a_deadline_has_no_countdown()
    {
        var doc = Create(out _, out _, out _);
        var start = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        doc.BeginElapsed(start, deadlineAt: null);
        doc.UpdateElapsed(start.AddSeconds(2));

        doc.ElapsedText.ShouldBe("2.0s elapsed");
        doc.HasDeadlineRemaining.ShouldBeFalse();
        doc.DeadlineRemainingText.ShouldBeNull();
    }

    // ── CU-2 FR-074: save response to file ───────────────────────────────────

    [Fact]
    public async Task Save_response_writes_the_response_json_to_the_picked_path()
    {
        var doc = CreateWithCapture(out var runner, out var picker, out var sink, out _);
        runner.Result = OkResult();
        await doc.InvokeCommand.ExecuteAsync(null);
        doc.SaveResponseCommand.CanExecute(null).ShouldBeTrue();
        picker.SaveResult = "/tmp/response.json";

        await doc.SaveResponseCommand.ExecuteAsync(null);

        picker.LastSaveSuggestedName.ShouldBe("response.json");
        sink.ToString().ShouldBe("{ \"ok\": true }");
    }

    [Fact]
    public async Task Save_response_writes_nothing_when_the_picker_is_cancelled()
    {
        var doc = CreateWithCapture(out var runner, out var picker, out var sink, out _);
        runner.Result = OkResult();
        await doc.InvokeCommand.ExecuteAsync(null);
        picker.SaveResult = null; // user cancelled the dialog

        await doc.SaveResponseCommand.ExecuteAsync(null);

        sink.ToString().ShouldBeEmpty();
    }

    [Fact]
    public void Save_response_is_disabled_without_a_response()
    {
        var doc = CreateWithCapture(out _, out _, out _, out _);

        doc.SaveResponseCommand.CanExecute(null).ShouldBeFalse();
    }

    // ── CU-2 FR-095: suggestion → settings deep-link ─────────────────────────

    [Fact]
    public void Open_setting_link_opens_the_settings_tab()
    {
        var doc = CreateWithCapture(out _, out _, out _, out var host);

        doc.OpenSettingLinkCommand.Execute("network");

        host.SettingsOpened.ShouldBe(1);
    }

    [Fact]
    public void Open_setting_link_ignores_an_empty_link()
    {
        var doc = CreateWithCapture(out _, out _, out _, out var host);

        doc.OpenSettingLinkCommand.Execute(null);
        doc.OpenSettingLinkCommand.Execute(string.Empty);

        host.SettingsOpened.ShouldBe(0);
    }

    // ── CU-3 FR-114: a completed call is recorded in the console ──────────────

    [Fact]
    public async Task A_completed_call_appends_a_console_row_with_its_total_and_phases()
    {
        var console = new GrpCurl.Net.Studio.ViewModels.Panes.ConsoleViewModel();
        var runner = new FakeInvocationRunner { Result = OkResult() };
        var doc = new InvocationDocumentViewModel(
            Conn(), "pkg.Svc/Go", "{}", runner, new FakeDescriptorService(), new ImmediateUiDispatcher(),
            new FakeClipboardService(), new FakeDialogService(), new FakeLauncherService(), new FakeRequestValidator(),
            console: console);

        await doc.InvokeCommand.ExecuteAsync(null);

        var row = console.Calls.ShouldHaveSingleItem();
        row.Method.ShouldBe("pkg.Svc/Go");
        row.StatusName.ShouldBe("OK");
        row.IsError.ShouldBeFalse();
        row.Activity.Phases.ShouldContain(p => p.Phase == "Call");
    }
}
