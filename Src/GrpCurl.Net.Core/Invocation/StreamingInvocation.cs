using Google.Protobuf;
using Grpc.Core;

namespace GrpCurl.Net.Invocation;

/// <summary>
///     Wraps a streaming gRPC call so callers can access the response stream alongside
///     the headers (resolved early) and trailers (resolved after the stream completes).
///     Used for server-streaming, client-streaming, and bidi-streaming so verbose output
///     can emit headers/trailers uniformly with the unary case (CODE-REVIEW.md P2
///     "Response Headers/Trailers Parity").
/// </summary>
internal sealed class StreamingInvocationResult(
    Task<Metadata> headers,
    IAsyncEnumerable<IMessage> responseStream,
    Func<Metadata?> trailersAccessor,
    Action dispose) : IDisposable
{
    private readonly Action _dispose = dispose;
    private readonly Func<Metadata?> _trailersAccessor = trailersAccessor;

    public Task<Metadata> ResponseHeadersAsync { get; } = headers;

    public IAsyncEnumerable<IMessage> ResponseStream { get; } = responseStream;

    /// <summary>
    ///     Returns the trailers once the response stream has completed. Returns
    ///     <see langword="null"/> if accessed before completion or if trailers are unavailable.
    /// </summary>
    public Metadata? GetTrailers()
    {
        try
        {
            return _trailersAccessor();
        }
        catch
        {
            return null;
        }
    }

    public void Dispose() => _dispose();
}

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
    private readonly Action _dispose = dispose;
    private readonly Func<Metadata?> _trailersAccessor = trailersAccessor;

    public Task<Metadata> ResponseHeadersAsync { get; } = headers;

    public IMessage Response { get; } = response;

    public Metadata? GetTrailers()
    {
        try
        {
            return _trailersAccessor();
        }
        catch
        {
            return null;
        }
    }

    public void Dispose() => _dispose();
}
