namespace GrpCurl.Net.Exceptions;

/// <summary>
///     Exception thrown by command handlers to signal an error that should result in a specific exit code.
///     This replaces Environment.Exit() calls to allow proper cleanup and testability.
/// </summary>
/// <param name="message">The error message.</param>
/// <param name="exitCode">The exit code to return (default is 1).</param>
/// <param name="silent">If true, the error has already been displayed (e.g., as JSON) and should not be printed again.</param>
public sealed class GrpcCommandException(string message, int exitCode = 1, bool silent = false) : Exception(message)
{
    /// <summary>
    ///     Gets the exit code that should be returned to the operating system.
    /// </summary>
    public int ExitCode { get; } = exitCode;

    /// <summary>
    ///     Gets whether the error has already been displayed and should not be printed again.
    /// </summary>
    public bool Silent { get; } = silent;
}