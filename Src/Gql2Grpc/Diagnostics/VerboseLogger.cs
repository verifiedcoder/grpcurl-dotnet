using Spectre.Console;

namespace Gql2Grpc.Diagnostics;

/// <summary>
///     Thin wrapper around <see cref="AnsiConsole" /> for verbose diagnostics on <c>stderr</c>.
///     All output is bound to <see cref="Console.Error" />; stdout is reserved for the GraphQL response envelope.
/// </summary>
public sealed class VerboseLogger
{
    private readonly IAnsiConsole _console;

    /// <summary>Creates a logger that emits at or below the given <paramref name="level" />.</summary>
    public VerboseLogger(VerbosityLevel level)
    {
        Level = level;
        _console = AnsiConsole.Create(new AnsiConsoleSettings
        {
            Out = new AnsiConsoleOutput(Console.Error)
        });
    }

    /// <summary>The verbosity threshold this logger is configured with.</summary>
    public VerbosityLevel Level { get; }

    /// <summary><c>true</c> if <see cref="Verbose(string)" /> calls will be emitted.</summary>
    public bool IsVerbose
        => Level >= VerbosityLevel.Verbose;

    /// <summary><c>true</c> if <see cref="VeryVerbose(string)" /> calls will be emitted.</summary>
    public bool IsVeryVerbose 
        => Level >= VerbosityLevel.VeryVerbose;

    /// <summary>Writes a dim-styled line to stderr when <see cref="IsVerbose" /> is set.</summary>
    public void Verbose(string message)
    {
        if (IsVerbose)
        {
            _console.MarkupLine($"[dim]{Markup.Escape(message)}[/]");
        }
    }

    /// <summary>Writes a dim-italic line to stderr when <see cref="IsVeryVerbose" /> is set.</summary>
    public void VeryVerbose(string message)
    {
        if (IsVeryVerbose)
        {
            _console.MarkupLine($"[dim italic]{Markup.Escape(message)}[/]");
        }
    }
}