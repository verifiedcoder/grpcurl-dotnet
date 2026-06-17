using GrpCurl.Net.Studio.Conformance;

namespace GrpCurl.Net.Studio.Tests.Unit;

/// <summary>
///     Deterministic regression cover for the conformance adapter's cancel-after-N invariant
///     (<see cref="TestCaseRunner.ShouldRecordResponse" />). A server-stream/bidi cancel-after-responses
///     test must report <em>exactly</em> N payloads: once the threshold is hit and the call is cancelled, a
///     message already in flight on the HTTP/2 stream must be ignored. This used to be covered only racily by
///     the conformance suite (it surfaced as an intermittent "expecting 1 ... got 2" flake on macOS).
/// </summary>
public sealed class ConformanceCancelInvariantTests
{
    [Fact]
    public void With_no_cancellation_every_response_is_recorded()
    {
        // afterNumResponses == 0 means "no cancel" — record all, however many arrive.
        TestCaseRunner.ShouldRecordResponse(received: 0, afterNumResponses: 0).ShouldBeTrue();
        TestCaseRunner.ShouldRecordResponse(received: 5, afterNumResponses: 0).ShouldBeTrue();
        TestCaseRunner.ShouldRecordResponse(received: 99, afterNumResponses: 0).ShouldBeTrue();
    }

    [Fact]
    public void Responses_are_recorded_up_to_the_cancel_threshold()
    {
        // Cancel after 1: record the first (received == 0), then stop.
        TestCaseRunner.ShouldRecordResponse(received: 0, afterNumResponses: 1).ShouldBeTrue();
        TestCaseRunner.ShouldRecordResponse(received: 1, afterNumResponses: 1).ShouldBeFalse(); // the in-flight extra
        TestCaseRunner.ShouldRecordResponse(received: 2, afterNumResponses: 1).ShouldBeFalse();
    }

    [Fact]
    public void A_message_arriving_after_a_higher_threshold_is_ignored()
    {
        // Cancel after 3: records 3 (received 0,1,2), ignores anything from received == 3 on.
        TestCaseRunner.ShouldRecordResponse(received: 0, afterNumResponses: 3).ShouldBeTrue();
        TestCaseRunner.ShouldRecordResponse(received: 2, afterNumResponses: 3).ShouldBeTrue();
        TestCaseRunner.ShouldRecordResponse(received: 3, afterNumResponses: 3).ShouldBeFalse(); // the in-flight extra
    }
}
