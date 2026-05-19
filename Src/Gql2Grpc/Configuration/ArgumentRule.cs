namespace Gql2Grpc.Configuration;

/// <summary>Discriminated union of per-argument rewrite rules supplied by a <see cref="MappingEntry" />.</summary>
public abstract record ArgumentRule
{
    /// <summary>Rename a GraphQL argument to a different gRPC field at the top of the request body.</summary>
    /// <param name="GrpcFieldName">Target gRPC field name.</param>
    public sealed record Rename(string GrpcFieldName) : ArgumentRule;

    /// <summary>Place a GraphQL argument's value at a nested gRPC request path (dot-separated).</summary>
    /// <param name="Path">Dot-separated nested field path inside the gRPC request message.</param>
    public sealed record PathRule(string Path) : ArgumentRule;

    /// <summary>Substitute a constant literal value for an argument, regardless of what the client supplied.</summary>
    /// <param name="Value">Literal JSON value (already serialized) inserted into the gRPC request.</param>
    public sealed record Literal(string Value) : ArgumentRule;

    /// <summary>Drop an argument entirely; never forward it to the gRPC request body.</summary>
    public sealed record SkipArgument : ArgumentRule;
}