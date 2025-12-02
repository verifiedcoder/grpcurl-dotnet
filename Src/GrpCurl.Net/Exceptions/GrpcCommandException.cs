namespace GrpCurl.Net.Exceptions;

/// <summary>
///     Exception thrown by command handlers to signal an error that should result in a specific exit code.
///     This replaces Environment.Exit() calls to allow proper cleanup and testability.
/// </summary>
/// <param name="message">The error message.</param>
/// <param name="exitCode">The exit code to return (default is 1).</param>
public sealed class GrpcCommandException(string message, int exitCode = 1) : Exception(message)
{
    /// <summary>
    ///     Gets the exit code that should be returned to the operating system.
    /// </summary>
    public int ExitCode { get; } = exitCode;
}