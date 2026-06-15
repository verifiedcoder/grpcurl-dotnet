namespace Gql2Grpc.Translation;

/// <summary>
///     Raised when a caller-supplied GraphQL argument, resolved by convention (no explicit
///     mapping rule), targets a field that does not exist on the gRPC request message. In
///     convention mode such arguments were previously written to the request unconditionally
///     and silently dropped server-side, so a typo'd or wrong-shaped argument produced a
///     vacuously successful call (review finding F4).
/// </summary>
public sealed class UnknownArgumentException(string argumentName, string attemptedPath, string requestTypeName)
    : Exception(BuildMessage(argumentName, attemptedPath, requestTypeName))
{
    public string ArgumentName { get; } = argumentName;

    public string AttemptedPath { get; } = attemptedPath;

    public string RequestTypeName { get; } = requestTypeName;

    private static string BuildMessage(string argumentName, string attemptedPath, string requestTypeName)
    {
        var pathNote = string.Equals(argumentName, attemptedPath, StringComparison.Ordinal)
            ? $"'{attemptedPath}'"
            : $"'{argumentName}' (resolved to '{attemptedPath}')";

        return $"Argument {pathNote} does not match any field of {requestTypeName}. " +
               "Add an explicit mapping rule for it, or correct the argument name.";
    }
}
