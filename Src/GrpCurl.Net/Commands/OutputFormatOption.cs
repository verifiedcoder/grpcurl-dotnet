using System.CommandLine;
using System.CommandLine.Parsing;

namespace GrpCurl.Net.Commands;

internal static class OutputFormatOption
{
    public const string Description =
        "Output format: 'text' (default, human-readable) or 'json' " +
        "(machine-readable; data on stdout, errors on stderr).";

    /// <summary>
    ///     Builds a fresh <c>--output</c> option. Each command needs its own instance
    ///     because System.CommandLine forbids sharing <see cref="Option" /> instances.
    /// </summary>
    public static Option<OutputFormat> Build()
        => new("--output")
        {
            Description = Description,
            DefaultValueFactory = _ => OutputFormat.Text,
            CustomParser = ParseOutputFormat
        };

    private static OutputFormat ParseOutputFormat(ArgumentResult result)
    {
        if (result.Tokens.Count == 0)
        {
            return OutputFormat.Text;
        }

        var token = result.Tokens[0].Value;

        switch (token.ToLowerInvariant())
        {
            case "text":

                return OutputFormat.Text;

            case "json":

                return OutputFormat.Json;

            default:

                result.AddError($"Invalid --output value '{token}'. Valid values: text, json.");

                return OutputFormat.Text;
        }
    }
}