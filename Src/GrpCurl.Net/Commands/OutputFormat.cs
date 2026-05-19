namespace GrpCurl.Net.Commands;

/// <summary>
///     Output format for command results.
/// </summary>
internal enum OutputFormat
{
    /// <summary>Human-readable Spectre.Console output (default).</summary>
    Text,

    /// <summary>
    ///     Machine-readable. Command data is emitted as line-based JSON envelopes on
    ///     stdout; errors as one-line JSON on stderr.
    /// </summary>
    Json
}