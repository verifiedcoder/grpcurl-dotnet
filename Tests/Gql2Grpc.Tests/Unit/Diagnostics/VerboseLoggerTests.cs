using Gql2Grpc.Diagnostics;

namespace Gql2Grpc.Tests.Unit.Diagnostics;

public sealed class VerboseLoggerTests
{
    [Fact]
    public void A_sink_receives_plain_verbose_lines_at_the_configured_level()
    {
        var lines = new List<string>();
        var logger = new VerboseLogger(VerbosityLevel.Verbose, lines.Add);

        logger.Verbose("resolved mapping");
        logger.VeryVerbose("request json"); // below the threshold — not emitted

        lines.ShouldBe(["resolved mapping"]);
    }

    [Fact]
    public void A_very_verbose_sink_receives_both_levels_in_order()
    {
        var lines = new List<string>();
        var logger = new VerboseLogger(VerbosityLevel.VeryVerbose, lines.Add);

        logger.Verbose("a");
        logger.VeryVerbose("b");

        lines.ShouldBe(["a", "b"]);
    }

    [Fact]
    public void A_quiet_sink_receives_nothing()
    {
        var lines = new List<string>();
        var logger = new VerboseLogger(VerbosityLevel.Quiet, lines.Add);

        logger.Verbose("a");
        logger.VeryVerbose("b");

        lines.ShouldBeEmpty();
    }
}
