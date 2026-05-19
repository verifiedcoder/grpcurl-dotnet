using Spectre.Console;

namespace GrpCurl.Net.Tests.Unit.Fixtures;

/// <summary>
///     Test helpers for capturing console output without touching the process-wide
///     <see cref="Console.Out"/> or <see cref="Console.Error"/> streams. This avoids
///     races between parallel xUnit test classes that would otherwise leave a disposed
///     <see cref="StringWriter"/> bound as the global stderr writer.
/// </summary>
internal static class TestConsole
{
    /// <summary>
    ///     Creates a non-interactive <see cref="IAnsiConsole"/> that writes into the given
    ///     <see cref="StringWriter"/>. The result is safe to use from any test in parallel
    ///     with any other test because it does not touch <see cref="Console.Error"/>.
    /// </summary>
    public static IAnsiConsole Create(StringWriter writer)
        => AnsiConsole.Create(new AnsiConsoleSettings
        {
            Out = new AnsiConsoleOutput(writer),
            Ansi = AnsiSupport.No,
            ColorSystem = ColorSystemSupport.NoColors,
            Interactive = InteractionSupport.No
        });

    /// <summary>
    ///     Splits captured output into lines using whichever newline form the platform
    ///     emitted (CRLF, LF, or CR). Returns an array with no empty trailing entry.
    /// </summary>
    public static string[] SplitLines(string output)
        => output.ReplaceLineEndings("\n").TrimEnd('\n').Split('\n');
}
