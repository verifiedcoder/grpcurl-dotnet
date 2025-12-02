using Spectre.Console;
using System.Diagnostics;

namespace GrpCurl.Net.Utilities;

/// <summary>
///     Tracks detailed timing information for gRPC operations.
///     Used with --vv/--very-verbose flag to provide performance insights.
/// </summary>
internal sealed class TimingContext
{
    private readonly Stopwatch _overallStopwatch = new();
    private readonly Dictionary<string, long> _phaseTimings = [];
    private Stopwatch? _currentPhase;
    private string? _currentPhaseName;

    public TimingContext()
        => _overallStopwatch.Start();

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
    ///     Prints a formatted timing summary to the console using Spectre.Console.
    /// </summary>
    public void PrintSummary()
    {
        EndCurrentPhase();
        _overallStopwatch.Stop();

        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[bold cyan]═══════════════════════════════════════════════════════════[/]");
        AnsiConsole.MarkupLine("[bold cyan]                    Timing Summary                          [/]");
        AnsiConsole.MarkupLine("[bold cyan]═══════════════════════════════════════════════════════════[/]");

        // Print phase timings in order they were recorded
        foreach (var (phase, ms) in _phaseTimings)
        {
            var microseconds = ms * 1000;
            var percentage = _overallStopwatch.ElapsedMilliseconds > 0
                ? ms * 100.0 / _overallStopwatch.ElapsedMilliseconds
                : 0;

            AnsiConsole.MarkupLine(
                $"  [dim]{phase,-35}[/] [yellow]{ms,6}[/] ms " +
                $"[dim]({microseconds,9} μs)[/] [dim]{percentage,5:F1}%[/]"
            );
        }

        AnsiConsole.MarkupLine("[bold cyan]───────────────────────────────────────────────────────────[/]");
        AnsiConsole.MarkupLine(
            $"  [bold]Total Time[/]                              " +
            $"[bold yellow]{_overallStopwatch.ElapsedMilliseconds,6}[/] [bold]ms[/]"
        );

        // Print additional metrics if available
        if (RequestSizeBytes > 0 || ResponseSizeBytes > 0 || MessageCount > 0)
        {
            AnsiConsole.MarkupLine("[bold cyan]───────────────────────────────────────────────────────────[/]");

            if (RequestSizeBytes > 0)
            {
                AnsiConsole.MarkupLine($"  [dim]Request Size:[/]  {FormatBytes(RequestSizeBytes)}");
            }

            if (ResponseSizeBytes > 0)
            {
                AnsiConsole.MarkupLine($"  [dim]Response Size:[/] {FormatBytes(ResponseSizeBytes)}");
            }

            if (MessageCount > 0)
            {
                AnsiConsole.MarkupLine($"  [dim]Message Count:[/] {MessageCount}");
            }
        }

        AnsiConsole.MarkupLine("[bold cyan]═══════════════════════════════════════════════════════════[/]");
    }

    /// <summary>
    ///     Formats byte count into human-readable format (bytes, KB, MB).
    /// </summary>
    private static string FormatBytes(long bytes)
        => bytes switch
        {
            < 1024 => $"[yellow]{bytes}[/] bytes",
            < 1024 * 1024 => $"[yellow]{bytes / 1024.0:F2}[/] KB [dim]({bytes:N0} bytes)[/]",
            _ => $"[yellow]{bytes / (1024.0 * 1024):F2}[/] MB [dim]({bytes:N0} bytes)[/]"
        };
}