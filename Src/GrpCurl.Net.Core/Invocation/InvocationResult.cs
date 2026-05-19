using Google.Protobuf;
using Grpc.Core;

namespace GrpCurl.Net.Invocation;

/// <summary>
///     Response wrapper that includes the message and gRPC metadata (headers/trailers).
/// </summary>
internal sealed class InvocationResult
{
    public required IMessage Response { get; init; }

    public Metadata? ResponseHeaders { get; init; }

    public Metadata? ResponseTrailers { get; init; }
}