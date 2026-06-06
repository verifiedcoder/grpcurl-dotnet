using GrpCurl.Net.Commands;
using System.CommandLine;

namespace GrpCurl.Net.Tests.Unit.Commands;

/// <summary>
///     Verifies the documented usage exit code (2) for System.CommandLine parse errors,
///     and that each error message is printed exactly once (the default parse-error action
///     returned exit code 1 and double-printed missing-argument errors).
/// </summary>
public sealed class ParseErrorReporterTests
{
    private const string HelpHint = "Run 'grpcurl.net [command] --help' for usage.";

    private static RootCommand BuildRootCommand() => new("test root")
    {
        ListCommandHandler.Create(),
        DescribeCommandHandler.Create(),
        InvokeCommandHandler.Create()
    };

    [Fact]
    public void TryHandleParseErrors_MissingRequiredArguments_ReturnsUsageExitCode()
    {
        // Arrange
        var parseResult = BuildRootCommand().Parse(["invoke"]);
        using var writer = new StringWriter();

        // Act
        var exitCode = ParseErrorReporter.TryHandleParseErrors(parseResult, writer, HelpHint);

        // Assert
        exitCode.ShouldBe(2);
    }

    [Fact]
    public void TryHandleParseErrors_MissingRequiredArguments_PrintsEachMessageOnce()
    {
        // Arrange
        var parseResult = BuildRootCommand().Parse(["invoke"]);
        using var writer = new StringWriter();

        // Act
        ParseErrorReporter.TryHandleParseErrors(parseResult, writer, HelpHint);

        // Assert
        var lines = writer.ToString()
            .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries)
            .Where(line => line != HelpHint)
            .ToArray();

        lines.ShouldNotBeEmpty();
        lines.Length.ShouldBe(lines.Distinct().Count());
    }

    [Fact]
    public void TryHandleParseErrors_UnknownOption_ReturnsUsageExitCodeAndHint()
    {
        // Arrange — both positional arguments are bound, so the option-like token
        // cannot be swallowed by an argument and must surface as a parse error.
        var parseResult = BuildRootCommand().Parse(["invoke", "localhost:9090", "svc/Method", "--no-such-option"]);
        using var writer = new StringWriter();

        // Act
        var exitCode = ParseErrorReporter.TryHandleParseErrors(parseResult, writer, HelpHint);

        // Assert
        exitCode.ShouldBe(2);
        writer.ToString().ShouldContain(HelpHint);
    }

    [Fact]
    public void TryHandleParseErrors_ValidParse_ReturnsNullAndWritesNothing()
    {
        // Arrange
        var parseResult = BuildRootCommand().Parse(["list", "--plaintext", "localhost:9090"]);
        using var writer = new StringWriter();

        // Act
        var exitCode = ParseErrorReporter.TryHandleParseErrors(parseResult, writer, HelpHint);

        // Assert
        exitCode.ShouldBeNull();
        writer.ToString().ShouldBeEmpty();
    }

    [Theory]
    [InlineData("--help")]
    [InlineData("--version")]
    public void TryHandleParseErrors_HelpAndVersion_ReturnNull(string flag)
    {
        // Arrange
        var parseResult = BuildRootCommand().Parse([flag]);
        using var writer = new StringWriter();

        // Act
        var exitCode = ParseErrorReporter.TryHandleParseErrors(parseResult, writer, HelpHint);

        // Assert
        exitCode.ShouldBeNull();
        writer.ToString().ShouldBeEmpty();
    }
}
