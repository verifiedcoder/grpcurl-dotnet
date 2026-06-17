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
}
