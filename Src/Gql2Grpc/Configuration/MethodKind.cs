namespace Gql2Grpc.Configuration;

/// <summary>Streaming kind of a gRPC method that <c>gql2grpc</c> can route to.</summary>
public enum MethodKind
{
    /// <summary>Single request, single response.</summary>
    Unary,

    /// <summary>Single request, server-pushed stream of responses.</summary>
    ServerStreaming
}