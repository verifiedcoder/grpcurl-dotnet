using Grpc.Core;
using GrpCurl.Net.Invocation;

namespace GrpCurl.Net.Tests.Unit.Invocation;

/// <summary>
///     Unit guard for the client-cancellation normalization (CU-6): a client-initiated cancel that races
///     the HTTP/2 teardown surfaces as UNAVAILABLE ("The request was aborted.") and must be reported as
///     CANCELLED — but only when the caller actually cancelled. Deterministic where conformance is racy.
/// </summary>
public sealed class RpcErrorNormalizerTests
{
    private static RpcException Unavailable(string detail)
        => new(new Status(StatusCode.Unavailable, detail));

    [Fact]
    public void Aborted_request_during_a_caller_cancel_becomes_cancelled()
    {
        var ex = Unavailable("Error reading next message. IOException: The request was aborted.");

        var result = RpcErrorNormalizer.Normalize(ex, deadline: null, cancellationRequested: true);

        result.StatusCode.ShouldBe(StatusCode.Cancelled);
    }

    [Fact]
    public void Aborted_request_without_a_caller_cancel_is_left_unavailable()
    {
        var ex = Unavailable("Error reading next message. IOException: The request was aborted.");

        var result = RpcErrorNormalizer.Normalize(ex, deadline: null, cancellationRequested: false);

        result.StatusCode.ShouldBe(StatusCode.Unavailable);
    }

    [Fact]
    public void A_genuine_unavailable_during_a_cancel_is_not_masked()
    {
        // A real server-unreachable failure (not the aborted-request shape) stays UNAVAILABLE even mid-cancel.
        var ex = Unavailable("Connection refused (localhost:5000)");

        var result = RpcErrorNormalizer.Normalize(ex, deadline: null, cancellationRequested: true);

        result.StatusCode.ShouldBe(StatusCode.Unavailable);
    }

    #region IsCancellationArtifact

    // Used by the duplex request producer to recognise the failure its OWN abort caused, so it can keep
    // a genuine write fault instead of the teardown status that fault provoked (PRD-003). Cancelling a
    // grpc-dotnet call produces either shape depending on whether teardown beats token propagation, so
    // covering only one of them makes the behaviour depend on a race — the exact gap round 5 found.
    // These are the deterministic guards for both shapes; the transport tests cannot pin which occurs.

    [Fact]
    public void Cancelled_is_a_cancellation_artifact()
        => RpcErrorNormalizer.IsCancellationArtifact(new RpcException(new Status(StatusCode.Cancelled, string.Empty)))
            .ShouldBeTrue();

    [Fact]
    public void Aborted_request_unavailable_is_a_cancellation_artifact()
        => RpcErrorNormalizer.IsCancellationArtifact(
                Unavailable("Error reading next message. IOException: The request was aborted."))
            .ShouldBeTrue();

    [Fact]
    public void A_genuine_server_unavailable_is_not_a_cancellation_artifact()
        => RpcErrorNormalizer.IsCancellationArtifact(Unavailable("Connection refused (localhost:5000)"))
            .ShouldBeFalse();

    [Theory]
    [InlineData(StatusCode.Internal)]
    [InlineData(StatusCode.ResourceExhausted)]
    [InlineData(StatusCode.DeadlineExceeded)]
    [InlineData(StatusCode.OK)]
    public void A_meaningful_status_is_not_a_cancellation_artifact(StatusCode status)
        => RpcErrorNormalizer.IsCancellationArtifact(new RpcException(new Status(status, "boom")))
            .ShouldBeFalse();

    #endregion
}
