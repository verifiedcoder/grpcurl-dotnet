namespace Gql2Grpc.Configuration;

/// <summary>Reshape rules applied to the gRPC response before projection into the GraphQL envelope.</summary>
public sealed record ResponseShaping
{
    /// <summary>Optional gRPC field name whose value replaces the entire response payload.</summary>
    public string? Unwrap { get; init; }
}