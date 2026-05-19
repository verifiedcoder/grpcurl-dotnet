namespace Gql2Grpc.GraphQL;

/// <summary>The three GraphQL operation kinds — matches the operation keyword in the document.</summary>
// ReSharper disable once InconsistentNaming
public enum GraphQLOperationType
{
    /// <summary>A read-only operation (<c>query { ... }</c>) that maps to a unary gRPC call.</summary>
    Query,

    /// <summary>A mutating operation (<c>mutation { ... }</c>) that maps to a unary gRPC call.</summary>
    Mutation,

    /// <summary>A long-lived operation (<c>subscription { ... }</c>) that maps to a server-streaming gRPC call.</summary>
    Subscription
}