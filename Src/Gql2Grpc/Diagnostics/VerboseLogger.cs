using Spectre.Console;

namespace Gql2Grpc.Diagnostics;

/// <summary>
///     Thin wrapper around <see cref="AnsiConsole" /> for verbose diagnostics. By default all output is
///     bound to <see cref="Console.Error" /> (stdout is reserved for the GraphQL response envelope). A host
///     may instead supply a <see cref="Action{T}" /> sink to capture the plain message text — e.g. Studio's
///     verbose pane (GQL-029) — without stderr or Spectre markup.
/// </summary>
public sealed class VerboseLogger
{
    private readonly IAnsiConsole? _console;
    private readonly Action<string>? _sink;

    /// <summary>Creates a logger that emits at or below the given <paramref name="level" />.</summary>
    /// <param name="level">The verbosity threshold.</param>
    /// <param name="sink">
    ///     Optional capture target for the plain message text. When supplied, lines go to it instead of
    ///     stderr (no markup). It may be invoked from worker threads, so it must be thread-safe.
    /// </param>
    public VerboseLogger(VerbosityLevel level, Action<string>? sink = null)
    {
        Level = level;
        _sink = sink;

        if (sink is null)
        {
            _console = AnsiConsole.Create(new AnsiConsoleSettings
            {
                Out = new AnsiConsoleOutput(Console.Error)
            });
        }
    }

    /// <summary>The verbosity threshold this logger is configured with.</summary>
    public VerbosityLevel Level { get; }

    /// <summary><c>true</c> if <see cref="Verbose(string)" /> calls will be emitted.</summary>
    public bool IsVerbose
        => Level >= VerbosityLevel.Verbose;

    /// <summary><c>true</c> if <see cref="VeryVerbose(string)" /> calls will be emitted.</summary>
    public bool IsVeryVerbose 
        => Level >= VerbosityLevel.VeryVerbose;

    /// <summary>Emits a verbose line (dim on stderr, or the plain text to the sink) when <see cref="IsVerbose" /> is set.</summary>
    public void Verbose(string message)
    {
        if (IsVerbose)
        {
            Emit(message, italic: false);
        }
    }

    /// <summary>Emits a very-verbose line (dim-italic on stderr, or the plain text to the sink) when <see cref="IsVeryVerbose" /> is set.</summary>
    public void VeryVerbose(string message)
    {
        if (IsVeryVerbose)
        {
            Emit(message, italic: true);
        }
    }

    private void Emit(string message, bool italic)
    {
        if (_sink is not null)
        {
            _sink(message);
            return;
        }

        var style = italic ? "dim italic" : "dim";
        _console!.MarkupLine($"[{style}]{Markup.Escape(message)}[/]");
    }
}