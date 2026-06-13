using GrpCurl.Net.Exceptions;

namespace GrpCurl.Net.Commands;

/// <summary>
///     Rejects option-like tokens that System.CommandLine silently bound to a positional
///     argument. The permissive arity on <c>address</c>/<c>service</c>/<c>symbol</c> means
///     an unrecognised <c>--flag</c> matches a positional instead of producing a parse
///     error, surfacing later as a confusing transport failure (exit 78) rather than a
///     usage error (exit 2). Single-dash tokens are deliberately NOT rejected — the
///     grpcurl drop-in compatibility path depends on them.
/// </summary>
internal static class PositionalArgumentGuard
{
    /// <param name="commandName">The subcommand, used in the help suggestion.</param>
    /// <param name="output">Active output format for error rendering.</param>
    /// <param name="positionals">Pairs of (argument name, bound value) to inspect.</param>
    public static void RejectOptionLikeValues(
        string commandName,
        OutputFormat output,
        params (string Name, string? Value)[] positionals)
    {
        foreach (var (name, value) in positionals)
        {
            if (value is null || !value.StartsWith("--", StringComparison.Ordinal))
            {
                continue;
            }

            ErrorRenderer.RenderAndThrow(new ErrorEnvelope
            {
                Category = ErrorCategory.Usage,
                ExitCode = 2,
                Message = $"Unrecognized option '{value}' (bound to the '{name}' argument)",
                Suggestions =
                [
                    $"Run 'grpcn {commandName} --help' to see supported options"
                ]
            }, output);
        }
    }
}
