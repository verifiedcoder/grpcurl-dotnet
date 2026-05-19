using Spectre.Console;
using System.Diagnostics;

namespace GrpCurl.Net.Utilities;

/// <summary>
///     Tracks detailed timing information for gRPC operations.
///     Used with --vv/--very-verbose flag to provide performance insights.
/// </summary>
/// <remarks>
///     The output sink is captured at construction time so that tests can inject a
///     <see cref="StringWriter" />-backed <see cref="IAnsiConsole" /> without touching the
///     process-wide <see cref="Console.Error" /> stream. Production callers should pass
///     <see cref="Diagnostics.CreateStderr" /> at the moment the context is created.
/// </remarks>
internal sealed class TimingContext
{
    private readonly IAnsiConsole _console;
    private readonly Stopwatch _overallStopwatch = new();
    private readonly Dictionary<string, long> _phaseTimings = [];
    private Stopwatch? _currentPhase;
    private string? _currentPhaseName;

    public TimingContext()
        : this(Diagnostics.CreateStderr())
    {
    }

    public TimingContext(IAnsiConsole console)
    {
        _console = console ?? throw new ArgumentNullException(nameof(console));
        _overallStopwatch.Start();
    }

    public long RequestSizeBytes { get; set; }

    public long ResponseSizeBytes { get; set; }

    public int MessageCount { get; set; }

    /// <summary>
    ///     Starts timing a new phase. Automatically ends the previous phase if one was running.
    /// </summary>
    /// <param name="phaseName">Name of the phase to track (e.g., "Connection Establishment")</param>
    public void StartPhase(string phaseName)
    {
        EndCurrentPhase();

        _currentPhaseName = phaseName;
        _currentPhase = Stopwatch.StartNew();
    }

    // Ends the currently running phase and records its timing.
    private void EndCurrentPhase()
    {
        if (_currentPhase is null || _currentPhaseName is null)
        {
            return;
        }

        _currentPhase.Stop();
        _phaseTimings[_currentPhaseName] = _currentPhase.ElapsedMilliseconds;
        _currentPhase = null;
        _currentPhaseName = null;
    }

    /// <summary>
    ///     Prints a formatted timing summary to the captured console using Spectre.Console.
    /// </summary>
    public void PrintSummary()
    {
        EndCurrentPhase();
        _overallStopwatch.Stop();

        _console.WriteLine();
        _console.MarkupLine("[bold cyan]═══════════════════════════════════════════════════════════[/]");
        _console.MarkupLine("[bold cyan]                    Timing Summary                          [/]");
        _console.MarkupLine("[bold cyan]═══════════════════════════════════════════════════════════[/]");

        foreach (var (phase, ms) in _phaseTimings)
        {
            var microseconds = ms * 1000;
            var percentage = _overallStopwatch.ElapsedMilliseconds > 0
                ? ms * 100.0 / _overallStopwatch.ElapsedMilliseconds
                : 0;

            _console.MarkupLine(
                $"  [dim]{phase,-35}[/] [yellow]{ms,6}[/] ms " +
                $"[dim]({microseconds,9} μs)[/] [dim]{percentage,5:F1}%[/]"
            );
        }

        _console.MarkupLine("[bold cyan]───────────────────────────────────────────────────────────[/]");
        _console.MarkupLine(
            $"  [bold]Total Time[/]                              " +
            $"[bold yellow]{_overallStopwatch.ElapsedMilliseconds,6}[/] [bold]ms[/]"
        );

        if (RequestSizeBytes > 0 || ResponseSizeBytes > 0 || MessageCount > 0)
        {
            _console.MarkupLine("[bold cyan]───────────────────────────────────────────────────────────[/]");

            if (RequestSizeBytes > 0)
            {
                _console.MarkupLine($"  [dim]Request Size:[/]  {FormatBytes(RequestSizeBytes)}");
            }

            if (ResponseSizeBytes > 0)
            {
                _console.MarkupLine($"  [dim]Response Size:[/] {FormatBytes(ResponseSizeBytes)}");
            }

            if (MessageCount > 0)
            {
                _console.MarkupLine($"  [dim]Message Count:[/] {MessageCount}");
            }
        }

        _console.MarkupLine("[bold cyan]═══════════════════════════════════════════════════════════[/]");
    }

    /// <summary>
    ///     Formats byte count into human-readable format (bytes, KB, MB).
    /// </summary>
    internal static string FormatBytes(long bytes)
        => bytes switch
        {
            < 1024        => $"[yellow]{bytes}[/] bytes",
            < 1024 * 1024 => $"[yellow]{bytes / 1024.0:F2}[/] KB [dim]({bytes:N0} bytes)[/]",
            _             => $"[yellow]{bytes / (1024.0 * 1024):F2}[/] MB [dim]({bytes:N0} bytes)[/]"
        };
}