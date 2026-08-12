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
    ///     Releases the underlying call, and then the request producer's token sources.
    /// </summary>
    /// <remarks>
    ///     Deliberately still <see cref="IDisposable" />, where <see cref="StreamingInvocationResult" />
    ///     is <see cref="IAsyncDisposable" />-only. That type has to <i>drain</i> its producer at
    ///     disposal, which a synchronous <c>Dispose</c> cannot do, so it omits the synchronous
    ///     interface to make the compiler reject <c>using</c> everywhere. Here the response is already
    ///     materialised before this object exists — there is no post-return enumeration during which a
    ///     producer could still be doing anything the caller cares about — so the bounded drain has
    ///     already happened inside
    ///     <see cref="DynamicInvoker.InvokeClientStreamingWithMetadataAsync" />, and both remaining
    ///     steps are synchronous: release the call, then the producer's token sources.
    ///     <para>
    ///         Synchronous from the caller's side is not the same as "the producer is finished". A
    ///         source that ignores cancellation is deliberately left running, and
    ///         <c>ReleaseWhenIdle</c> defers the token-source disposal until it eventually exits. What
    ///         disposal guarantees is that the caller is not made to wait for it.
    ///     </para>
    ///     <para>
    ///         Idempotence is delegated to <c>GrpcCall.Dispose</c> and to the producer's own
    ///         <c>Interlocked</c> release guard; this type adds no non-idempotent teardown of its own.
    ///     </para>
    /// </remarks>
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
