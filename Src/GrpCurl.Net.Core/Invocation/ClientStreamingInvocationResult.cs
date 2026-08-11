using Google.Protobuf;
using Grpc.Core;

namespace GrpCurl.Net.Invocation;

/// <summary>
///     Variant for client-streaming RPCs: caller writes requests, server returns a single
///     response. Exposes the response together with headers and trailers.
/// </summary>
internal sealed class ClientStreamingInvocationResult(
    Task<Metadata> headers,
    IMessage response,
    Func<Metadata?> trailersAccessor,
    Action dispose) : IDisposable
{
    public Task<Metadata> ResponseHeadersAsync { get; } = headers;

    public IMessage Response { get; } = response;

    /// <summary>
    ///     Releases the underlying call. Idempotence is delegated to <c>GrpcCall.Dispose</c>, which is
    ///     guarded internally: unlike <see cref="StreamingInvocationResult" /> — whose own
    ///     <c>Interlocked</c> guard exists because it drains a request producer and releases token
    ///     sources — this type does no non-idempotent teardown of its own.
    /// </summary>
    public void Dispose() => dispose();

    public Metadata? GetTrailers()
    {
        try
        {
            return trailersAccessor();
        }
        catch
        {
            return null;
        }
    }
}