namespace GrpCurl.Net.Commands;

/// <summary>
///     Thrown when stdin input exceeds <c>--max-stdin-bytes</c>. Distinct from
///     <see cref="InvalidOperationException" /> so the command handler can classify it as a
///     usage error (exit 2) rather than an internal failure (exit 1).
/// </summary>
/// <param name="message">The error message.</param>
internal sealed class StdinLimitExceededException(string message) : Exception(message);
