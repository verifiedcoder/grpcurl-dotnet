using System.CommandLine;

namespace GrpCurl.Net.Commands;

/// <summary>
///     Maps System.CommandLine parse errors (unknown options, missing required arguments)
///     to the documented usage exit code (2). Handling parse errors here — instead of letting
///     <c>ParseResult.InvokeAsync</c> run its default error action — keeps the exit-code
///     contract accurate (the default action returns 1) and prevents the same message from
///     being printed twice.
/// </summary>
internal static class ParseErrorReporter
{
    internal const int UsageExitCode = 2;

    /// <summary>
    ///     Writes de-duplicated parse error messages plus a usage hint to
    ///     <paramref name="errorWriter" /> and returns exit code 2, or returns
    ///     <see langword="null" /> when the parse produced no errors (including
    ///     <c>--help</c>/<c>--version</c>, which parse cleanly and flow to their actions).
    /// </summary>
    /// <param name="parseResult">The parse result to inspect for errors.</param>
    /// <param name="errorWriter">Writer for diagnostics (stderr; stdout stays clean for data).</param>
    /// <param name="helpHint">One-line usage hint appended after the error messages.</param>
    public static int? TryHandleParseErrors(ParseResult parseResult, TextWriter errorWriter, string helpHint)
    {
        if (parseResult.Errors.Count == 0)
        {
            return null;
        }

        foreach (var message in parseResult.Errors.Select(e => e.Message).Distinct())
        {
            errorWriter.WriteLine(message);
        }

        errorWriter.WriteLine(helpHint);

        return UsageExitCode;
    }
}
