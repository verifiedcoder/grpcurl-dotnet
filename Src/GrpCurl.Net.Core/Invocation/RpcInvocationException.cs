using Grpc.Core;

namespace GrpCurl.Net.Invocation;

/// <summary>
///     An <see cref="RpcException" /> that additionally carries the response headers the
///     server sent before failing the call. Unary and client-streaming calls surface
///     errors before the caller can read <c>ResponseHeadersAsync</c>, so
///     <see cref="DynamicInvoker" /> captures the headers and rethrows with them attached;
///     existing <c>catch (RpcException)</c> handlers are unaffected.
/// </summary>
internal sealed class RpcInvocationException(RpcException inner, Metadata responseHeaders)
    : RpcException(inner.Status, inner.Trailers, inner.Message)
{
    /// <summary>Response headers received before the call failed.</summary>
    public Metadata ResponseHeaders { get; } = responseHeaders;
}
