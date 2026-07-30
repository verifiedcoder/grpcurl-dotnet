using Grpc.Core;

namespace GrpCurl.Net.Invocation;

/// <summary>
///     Normalizes <see cref="RpcException" /> status codes to the gRPC specification where
///     Grpc.Net.Client deviates. The matched messages are client-generated constants from
///     grpc-dotnet (never server-supplied data), so the remaps cannot misfire on genuine
///     server statuses. The connectrpc/conformance suite in CI guards these patterns
///     against grpc-dotnet version drift.
/// </summary>
internal static class RpcErrorNormalizer
{
    private const string BadHttpStatusPrefix = "Bad gRPC response. HTTP status code: ";

    /// <summary>
    ///     grpc-dotnet's client-generated detail for a request torn down under an in-flight call.
    ///     Never server-supplied, so matching on it cannot misfire on a genuine server status.
    /// </summary>
    private const string AbortedRequestDetail = "The request was aborted";

    private static readonly TimeSpan DeadlineSkewTolerance = TimeSpan.FromMilliseconds(50);

    /// <summary>
    ///     True when a failure is one of the two shapes cancelling a grpc-dotnet call can produce:
    ///     a plain CANCELLED, or the aborted-request UNAVAILABLE that appears when HTTP/2 teardown
    ///     beats cancellation-token propagation.
    ///     <para>
    ///         Callers that cancel a call themselves use this to recognise their own artifact. It
    ///         deliberately does not consider any token state — the caller knows whether it did the
    ///         cancelling; <see cref="Normalize" /> is the path that keys off the caller's token.
    ///         Because the UNAVAILABLE arm matches a client-generated detail string, a genuine server
    ///         UNAVAILABLE is never mistaken for one of ours.
    ///     </para>
    /// </summary>
    public static bool IsCancellationArtifact(RpcException exception)
        => exception.StatusCode == StatusCode.Cancelled || IsAbortedRequest(exception);

    private static bool IsAbortedRequest(RpcException exception)
        => exception.StatusCode == StatusCode.Unavailable
           && exception.Status.Detail.Contains(AbortedRequestDetail, StringComparison.Ordinal);

    public static RpcException Normalize(RpcException exception, DateTime? deadline, bool cancellationRequested = false)
    {
        var normalized = NormalizeDeadlineExpiry(exception, deadline);

        normalized = NormalizeClientCancellation(normalized, cancellationRequested);

        return NormalizeStackDeviations(normalized);
    }

    /// <summary>
    ///     Reports CANCELLED when the caller's cancellation token was signalled and the failure is the
    ///     aborted-request <see cref="System.IO.IOException" /> that grpc-dotnet surfaces as UNAVAILABLE.
    ///     The HTTP/2 stream teardown can beat the cancellation-token propagation, so a client-initiated
    ///     cancel intermittently lands as UNAVAILABLE ("The request was aborted.") instead of CANCELLED.
    ///     Gated on the caller's own cancellation, so it cannot reinterpret a genuine server UNAVAILABLE.
    /// </summary>
    private static RpcException NormalizeClientCancellation(RpcException exception, bool cancellationRequested)
    {
        if (cancellationRequested && IsAbortedRequest(exception))
        {
            return WithStatusCode(exception, StatusCode.Cancelled);
        }

        return exception;
    }

    /// <summary>
    ///     Reports DEADLINE_EXCEEDED for any RPC failure that surfaces after the deadline
    ///     elapsed, matching official gRPC clients. Covers the race where the server tears
    ///     the stream down at its own deadline (an HTTP/2 RST that would otherwise map to
    ///     CANCELLED) a moment before the local deadline timer fires.
    /// </summary>
    private static RpcException NormalizeDeadlineExpiry(RpcException exception, DateTime? deadline)
    {
        if (deadline is null || exception.StatusCode == StatusCode.DeadlineExceeded)
        {
            return exception;
        }

        var now = DateTime.UtcNow;

        if (now >= deadline.Value)
        {
            return WithStatusCode(exception, StatusCode.DeadlineExceeded);
        }

        // A bare HTTP/2 RST_STREAM(CANCEL) with no grpc-status is how gRPC servers abort
        // a call whose deadline fired. Timer granularity can land it a hair before the
        // locally computed deadline, so allow a small skew window for that exact shape.
        if (exception.StatusCode == StatusCode.Cancelled
            && now >= deadline.Value - DeadlineSkewTolerance
            && exception.Status.Detail.Contains("server reset the stream. HTTP/2 error code 'CANCEL'", StringComparison.Ordinal))
        {
            return WithStatusCode(exception, StatusCode.DeadlineExceeded);
        }

        return exception;
    }

    private static RpcException NormalizeStackDeviations(RpcException exception)
    {
        var detail = exception.Status.Detail;

        switch (exception.StatusCode)
        {
            // gRPC spec (PROTOCOL-HTTP2.md): when a response carries an HTTP error status
            // with no grpc-status, statuses outside the mapping table translate to
            // UNKNOWN. grpc-dotnet reports INTERNAL for all of them.
            case StatusCode.Internal when detail.StartsWith(BadHttpStatusPrefix, StringComparison.Ordinal)
                                          && int.TryParse(detail.AsSpan(BadHttpStatusPrefix.Length), out var httpStatus):
                return WithStatusCode(exception, MapHttpStatusToGrpcCode(httpStatus));

            // A second message on a unary/client-streaming response is a cardinality
            // violation: UNIMPLEMENTED per the spec (and grpc-go since v1.58);
            // grpc-dotnet reports INTERNAL.
            case StatusCode.Internal when detail.Contains("Unexpected data after finished reading message", StringComparison.Ordinal):
                return WithStatusCode(exception, StatusCode.Unimplemented);

            // An OK response with no message is likewise a cardinality violation:
            // UNIMPLEMENTED; grpc-dotnet reports INTERNAL.
            case StatusCode.Internal when detail.Contains("Failed to deserialize response message", StringComparison.Ordinal):
                return WithStatusCode(exception, StatusCode.Unimplemented);

            // A response missing grpc-status entirely is a broken gRPC response: UNKNOWN;
            // grpc-dotnet reports CANCELLED.
            case StatusCode.Cancelled when detail.Contains("No grpc-status found on response", StringComparison.Ordinal):
                return WithStatusCode(exception, StatusCode.Unknown);

            // A response with a non-gRPC content-type is not a gRPC response: UNKNOWN;
            // grpc-dotnet reports CANCELLED.
            case StatusCode.Cancelled when detail.Contains("Bad gRPC response. Invalid content-type value", StringComparison.Ordinal):
                return WithStatusCode(exception, StatusCode.Unknown);

            // A response compressed with an encoding the client never advertised is a
            // protocol violation by the server: INTERNAL; grpc-dotnet reports
            // UNIMPLEMENTED (which the spec reserves for the server to send).
            case StatusCode.Unimplemented when detail.Contains("Unsupported grpc-encoding value", StringComparison.Ordinal):
                return WithStatusCode(exception, StatusCode.Internal);

            default:
                return exception;
        }
    }

    /// <summary>HTTP-to-gRPC status mapping from PROTOCOL-HTTP2.md.</summary>
    private static StatusCode MapHttpStatusToGrpcCode(int httpStatus) => httpStatus switch
    {
        400 => StatusCode.Internal,
        401 => StatusCode.Unauthenticated,
        403 => StatusCode.PermissionDenied,
        404 => StatusCode.Unimplemented,
        429 or 502 or 503 or 504 => StatusCode.Unavailable,
        _ => StatusCode.Unknown
    };

    private static RpcException WithStatusCode(RpcException exception, StatusCode statusCode) =>
        statusCode == exception.StatusCode
            ? exception
            : new RpcException(
                new Status(statusCode, exception.Status.Detail, exception.Status.DebugException),
                exception.Trailers);
}
