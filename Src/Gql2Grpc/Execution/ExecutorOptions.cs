using Grpc.Core;

namespace Gql2Grpc.Execution;

internal sealed record ExecutorOptions
{
    public required Metadata RpcMetadata { get; init; }

    public DateTime? Deadline { get; init; }

    public bool EmitDefaults { get; init; }

    public bool AllowUnknownFields { get; init; } = true;

    public bool RawOutput { get; init; }

    public bool IntrospectionEnabled { get; init; } = true;
}