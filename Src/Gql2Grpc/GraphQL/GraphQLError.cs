namespace Gql2Grpc.GraphQL;

/// <summary>
///     GraphQL spec error record. <see cref="Path" /> is a JSON array of string/int segments
///     identifying the response field at which the error occurred (empty for top-level errors).
/// </summary>
/// <param name="Message">Human-readable error message rendered into <c>errors[].message</c>.</param>
/// <param name="Path">Response-key/index segments locating the error in the data tree.</param>
/// <param name="Extensions">Optional category/code metadata rendered into <c>errors[].extensions</c>.</param>
// ReSharper disable once InconsistentNaming
public sealed record GraphQLError(
    string Message,
    IReadOnlyList<object> Path,
    IReadOnlyDictionary<string, object?>? Extensions = null);