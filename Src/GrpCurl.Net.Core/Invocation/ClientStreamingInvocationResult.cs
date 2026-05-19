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