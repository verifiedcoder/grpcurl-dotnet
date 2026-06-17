using GrpCurl.Net.Studio.Services;
using GrpCurl.Net.Studio.Tests.Unit.Fakes;
using GrpCurl.Net.Studio.TestSupport;
using GrpCurl.Net.Studio.ViewModels.Models.Connections;
using GrpCurl.Net.Studio.ViewModels.Models.History;
using GrpCurl.Net.Studio.ViewModels.Models.Invocation;

namespace GrpCurl.Net.Studio.Tests.Unit;

/// <summary>E3.3 PR-B: recording invocations into history — redaction-at-rest (FR-121), body caps,
/// response opt-in, capture on/off, and outcome mapping.</summary>
public sealed class HistoryRecorderTests
{
    private CancellationToken Ct => TestContext.Current.CancellationToken;

    private static InvocationRequestModel Request(IReadOnlyList<HeaderEntry>? headers = null, string body = "{ \"x\": 1 }") => new(
        new SavedConnection { Name = "staging", Address = "api.example.com:443", Transport = TransportMode.Tls },
        "pkg.Svc/Go", body, headers ?? [], Deadline: "10s");

    private static InvocationResultModel OkResult(string? json = "{ \"ok\": true }") => new(
        Ok: true, ResponseJson: json, ResponseHeaders: [], ResponseTrailers: [],
        Status: new InvocationStatusModel(0, "OK", string.Empty),
        Timing: new TimingModel([new TimingPhase("total", TimeSpan.FromMilliseconds(42))], 0, 0), ErrorMessage: null);

    private static InvocationResultModel ErrorResult(int code = 5) => new(
        Ok: false, ResponseJson: null, ResponseHeaders: [], ResponseTrailers: [],
        Status: new InvocationStatusModel(code, "NotFound", "missing"),
        Timing: new TimingModel([], 0, 0), ErrorMessage: "missing",
        Error: new ErrorModel(ErrorCategoryKind.Rpc, code, "NotFound", StatusSeverity.Caller, "missing",
            Hint: null, "api.example.com:443", "pkg.Svc/Go", [], [], "{}"));

    private static HistoryRecorder Recorder(out FakeHistoryStore store, out FakeSettingsStore settings)
    {
        store = new FakeHistoryStore();
        settings = new FakeSettingsStore();
        return new HistoryRecorder(store, settings);
    }

    [Fact]
    public async Task Records_a_redacted_request_snapshot()
    {
        var recorder = Recorder(out var store, out _);
        var headers = new List<HeaderEntry>
        {
            new() { Name = "authorization", Value = "Bearer super-secret-token" },
            new() { Name = "x-trace-id", Value = "${TRACE}" }
        };

        await recorder.RecordUnaryAsync(Request(headers), OkResult(), Ct);

        var entry = store.Last.ShouldNotBeNull();
        entry.Kind.ShouldBe(HistoryKind.Grpc);
        entry.Connection.Transport.ShouldBe("tls");
        entry.Method.ShouldBe("pkg.Svc/Go");

        // FR-121: the secret value is gone; the ${VAR} placeholder is kept unexpanded.
        entry.Request.Headers.Single(h => h.Name == "authorization").Value.ShouldBe(HistoryEntry.RedactedMarker);
        entry.Request.Headers.Single(h => h.Name == "x-trace-id").Value.ShouldBe("${TRACE}");
        entry.Request.Headers.ShouldNotContain(h => h.Value.Contains("super-secret"));
    }

    [Fact]
    public async Task Maps_a_successful_outcome()
    {
        var recorder = Recorder(out var store, out _);

        await recorder.RecordUnaryAsync(Request(), OkResult(), Ct);

        var outcome = store.Last!.Outcome;
        outcome.Category.ShouldBe("success");
        outcome.ExitCodeEquivalent.ShouldBe(0);
        outcome.Status.ShouldBe("OK");
        outcome.DurationMs.ShouldBe(42);
        outcome.MessagesReceived.ShouldBe(1);
    }

    [Fact]
    public async Task Maps_an_rpc_failure_to_category_and_exit_code()
    {
        var recorder = Recorder(out var store, out _);

        await recorder.RecordUnaryAsync(Request(), ErrorResult(code: 5), Ct);

        var outcome = store.Last!.Outcome;
        outcome.Category.ShouldBe("rpc-error");
        outcome.ExitCodeEquivalent.ShouldBe(69); // 64 + 5
    }

    [Fact]
    public async Task Disabled_capture_records_nothing()
    {
        var recorder = Recorder(out var store, out var settings);
        settings.Current.History.Enabled = false;

        await recorder.RecordUnaryAsync(Request(), OkResult(), Ct);

        store.Entries.ShouldBeEmpty();
    }

    [Fact]
    public async Task Response_bodies_are_omitted_by_default_and_stored_when_opted_in()
    {
        var recorder = Recorder(out var store, out var settings);

        await recorder.RecordUnaryAsync(Request(), OkResult("{ \"a\": 1 }"), Ct);
        store.Last!.Outcome.ResponseBody.ShouldBeNull();

        settings.Current.History.CaptureResponses = true;
        await recorder.RecordUnaryAsync(Request(), OkResult("{ \"a\": 1 }"), Ct);
        store.Last!.Outcome.ResponseBody.ShouldBe("{ \"a\": 1 }");
    }

    [Fact]
    public async Task A_body_over_the_cap_is_truncated_at_a_utf8_boundary()
    {
        var recorder = Recorder(out var store, out var settings);
        settings.Current.History.ResponseCapBytes = 8;

        await recorder.RecordUnaryAsync(Request(body: "0123456789ABCDEF"), OkResult(), Ct);

        var request = store.Last!.Request;
        request.BodyTruncated.ShouldBeTrue();
        request.Body.ShouldBe("01234567"); // 8 bytes, cut at a char boundary
    }

    [Fact]
    public async Task Records_a_streaming_call_with_counts_and_status()
    {
        var recorder = Recorder(out var store, out _);
        var request = new StreamRequestModel(
            new SavedConnection { Name = "s", Address = "h:1", Transport = TransportMode.Plaintext }, "pkg.Svc/Chat", []);

        await recorder.RecordStreamAsync(request, new InvocationStatusModel(0, "OK", string.Empty),
            durationMs: 1200, messagesSent: 3, messagesReceived: 5, Ct);

        var entry = store.Last.ShouldNotBeNull();
        entry.Connection.Transport.ShouldBe("plaintext");
        entry.Outcome.MessagesSent.ShouldBe(3);
        entry.Outcome.MessagesReceived.ShouldBe(5);
        entry.Outcome.DurationMs.ShouldBe(1200);
        entry.Outcome.Category.ShouldBe("success");
    }
}
