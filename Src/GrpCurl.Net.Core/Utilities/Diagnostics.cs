using Spectre.Console;

namespace GrpCurl.Net.Utilities;

/// <summary>
///     Stderr-bound diagnostic output. All errors, warnings, hints, and verbose chatter
///     route through this helper so that stdout is reserved for command result data
///     (list entries, describe output, invoke response messages).
/// </summary>
/// <remarks>
///     <para>
///         A new <see cref="IAnsiConsole" /> is constructed per call so that tests which
///         redirect stderr via <see cref="Console.SetError" /> capture output written here.
///         The construction cost is negligible compared to actual diagnostic frequency.
///     </para>
/// </remarks>
internal static class Diagnostics
{
    /// <summary>Convenience accessor for an <see cref="IAnsiConsole" /> targeting stderr.</summary>
    public static IAnsiConsole Stderr => CreateStderr();

    /// <summary>Builds a fresh <see cref="IAnsiConsole" /> bound to the current <see cref="Console.Error" />.</summary>
    public static IAnsiConsole CreateStderr()
        => AnsiConsole.Create(new AnsiConsoleSettings
        {
            Out = new AnsiConsoleOutput(Console.Error)
        });

    /// <summary>Writes a Spectre.Console markup line to stderr.</summary>
    public static void Markup(string message)
        => CreateStderr().MarkupLine(message);

    /// <summary>Writes a plain (unformatted) line to stderr.</summary>
    public static void Plain(string message)
        => Console.Error.WriteLine(message);
}