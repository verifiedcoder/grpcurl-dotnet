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
internal sealed class StreamingInvocationResult : IDisposable
{
    private readonly Action _dispose;
    private readonly Func<Metadata?> _trailersAccessor;

    public StreamingInvocationResult(
        Task<Metadata> headers,
        IAsyncEnumerable<IMessage> responseStream,
        Func<Metadata?> trailersAccessor,
        Action dispose)
    {
        ResponseHeadersAsync = headers;
        ResponseStream = responseStream;
        _trailersAccessor = trailersAccessor;
        _dispose = dispose;
    }

    public Task<Metadata> ResponseHeadersAsync { get; }

    public IAsyncEnumerable<IMessage> ResponseStream { get; }

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
internal sealed class ClientStreamingInvocationResult : IDisposable
{
    private readonly Action _dispose;
    private readonly Func<Metadata?> _trailersAccessor;

    public ClientStreamingInvocationResult(
        Task<Metadata> headers,
        IMessage response,
        Func<Metadata?> trailersAccessor,
        Action dispose)
    {
        ResponseHeadersAsync = headers;
        Response = response;
        _trailersAccessor = trailersAccessor;
        _dispose = dispose;
    }

    public Task<Metadata> ResponseHeadersAsync { get; }

    public IMessage Response { get; }

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
