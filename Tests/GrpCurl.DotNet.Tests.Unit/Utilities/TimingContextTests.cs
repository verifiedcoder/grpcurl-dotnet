using GrpCurl.Net.Tests.Unit.Fixtures;
using GrpCurl.Net.Utilities;

namespace GrpCurl.Net.Tests.Unit.Utilities;

public sealed class TimingContextTests
{
    private static TimingContext NewContext(out StringWriter writer)
    {
        writer = new StringWriter();

        var console = TestConsole.Create(writer);

        return new TimingContext(console);
    }

    #region Phases accessor (SPEC-031)

    [Fact]
    public void Phases_ReturnsCompletedPhasesInOrder()
    {
        // Arrange
        var context = NewContext(out _);

        context.StartPhase("Connection");
        context.StartPhase("Schema Discovery"); // ends "Connection"
        context.StartPhase("Call");             // ends "Schema Discovery"

        // Act — before the final phase ends, only completed phases are present.
        var beforeEnd = context.Phases.Select(p => p.Phase).ToList();

        context.PrintSummary(); // ends "Call"
        var afterEnd = context.Phases.Select(p => p.Phase).ToList();

        // Assert
        beforeEnd.ShouldBe(["Connection", "Schema Discovery"]);
        afterEnd.ShouldBe(["Connection", "Schema Discovery", "Call"]);
        context.Phases.ShouldAllBe(p => p.Duration >= TimeSpan.Zero);
    }

    [Fact]
    public void Phases_EmptyBeforeAnyPhaseStarts()
    {
        var context = NewContext(out _);

        context.Phases.ShouldBeEmpty();
    }

    #endregion

    #region FormatBytes Tests

    [Theory]
    [InlineData(0, "[yellow]0[/] bytes")]
    [InlineData(1, "[yellow]1[/] bytes")]
    [InlineData(512, "[yellow]512[/] bytes")]
    [InlineData(1023, "[yellow]1023[/] bytes")]
    public void FormatBytes_LessThan1024_ReturnsBytesFormat(long bytes, string expected)
    {
        // Arrange

        // Act
        var result = TimingContext.FormatBytes(bytes);

        // Assert
        result.ShouldBe(expected);
    }

    [Fact]
    public void FormatBytes_Exactly1024_ReturnsKBFormat()
    {
        // Arrange

        // Act
        var result = TimingContext.FormatBytes(1024);

        // Assert
        result.ShouldContain("KB");
        result.ShouldContain("1.00");
        result.ShouldContain("1,024 bytes");
    }

    [Fact]
    public void FormatBytes_InKBRange_ReturnsKBFormat()
    {
        // Arrange

        // Act
        var result = TimingContext.FormatBytes(10240);

        // Assert
        result.ShouldContain("KB");
        result.ShouldContain("10.00");
        result.ShouldContain("10,240 bytes");
    }

    [Fact]
    public void FormatBytes_JustBelowMB_ReturnsKBFormat()
    {
        // Arrange

        // Act
        var result = TimingContext.FormatBytes(1024 * 1024 - 1);

        // Assert
        result.ShouldContain("KB");
        result.ShouldContain("1,048,575 bytes");
    }

    [Fact]
    public void FormatBytes_Exactly1MB_ReturnsMBFormat()
    {
        // Arrange

        // Act
        var result = TimingContext.FormatBytes(1024 * 1024);

        // Assert
        result.ShouldContain("MB");
        result.ShouldContain("1.00");
        result.ShouldContain("1,048,576 bytes");
    }

    [Fact]
    public void FormatBytes_LargeMBValue_ReturnsMBFormat()
    {
        // Arrange

        // Act
        var result = TimingContext.FormatBytes(10 * 1024 * 1024);

        // Assert
        result.ShouldContain("MB");
        result.ShouldContain("10.00");
        result.ShouldContain("10,485,760 bytes");
    }

    [Fact]
    public void FormatBytes_Zero_ReturnsBytesFormat()
    {
        // Arrange

        // Act
        var result = TimingContext.FormatBytes(0);

        // Assert
        result.ShouldBe("[yellow]0[/] bytes");
    }

    #endregion

    #region Properties Tests

    [Fact]
    public void RequestSizeBytes_DefaultValue_IsZero()
    {
        // Arrange

        // Act
        var context = NewContext(out _);

        // Assert
        context.RequestSizeBytes.ShouldBe(0);
    }

    [Fact]
    public void RequestSizeBytes_SetValue_RetainsValue()
    {
        // Arrange
        var context = NewContext(out _);

        // Act
        context.RequestSizeBytes = 4096;

        // Assert
        context.RequestSizeBytes.ShouldBe(4096);
    }

    [Fact]
    public void ResponseSizeBytes_DefaultValue_IsZero()
    {
        // Arrange

        // Act
        var context = NewContext(out _);

        // Assert
        context.ResponseSizeBytes.ShouldBe(0);
    }

    [Fact]
    public void ResponseSizeBytes_SetValue_RetainsValue()
    {
        // Arrange
        var context = NewContext(out _);

        // Act
        context.ResponseSizeBytes = 8192;

        // Assert
        context.ResponseSizeBytes.ShouldBe(8192);
    }

    [Fact]
    public void MessageCount_DefaultValue_IsZero()
    {
        // Arrange

        // Act
        var context = NewContext(out _);

        // Assert
        context.MessageCount.ShouldBe(0);
    }

    [Fact]
    public void MessageCount_SetValue_RetainsValue()
    {
        // Arrange
        var context = NewContext(out _);

        // Act
        context.MessageCount = 42;

        // Assert
        context.MessageCount.ShouldBe(42);
    }

    #endregion

    #region StartPhase / EndCurrentPhase Tests

    [Fact]
    public void StartPhase_SinglePhase_DoesNotThrow()
    {
        // Arrange

        // Act
        var context = NewContext(out _);

        // Assert
        Should.NotThrow(() => context.StartPhase("Test Phase"));
    }

    [Fact]
    public void StartPhase_MultiplePhases_DoesNotThrow()
    {
        // Arrange

        // Act
        var context = NewContext(out _);

        // Assert
        Should.NotThrow(() =>
        {
            context.StartPhase("Phase 1");
            context.StartPhase("Phase 2");
            context.StartPhase("Phase 3");
        });
    }

    [Fact]
    public void StartPhase_SameNameTwice_DoesNotThrow()
    {
        // Arrange

        // Act
        var context = NewContext(out _);

        // Assert
        Should.NotThrow(() =>
        {
            context.StartPhase("Connection");
            context.StartPhase("Connection");
        });
    }

    #endregion

    #region PrintSummary Tests

    [Fact]
    public void PrintSummary_NoPhases_DoesNotThrow()
    {
        // Arrange

        // Act
        var context = NewContext(out var writer);

        // Assert
        Should.NotThrow(() => context.PrintSummary());

        writer.ToString().ShouldContain("Timing Summary");
    }

    [Fact]
    public void PrintSummary_WithPhases_DoesNotThrow()
    {
        // Arrange
        var context = NewContext(out var writer);

        context.StartPhase("Connection Establishment");

        // Act
        context.StartPhase("RPC Call");

        // Assert
        Should.NotThrow(() => context.PrintSummary());

        var output = writer.ToString();

        output.ShouldContain("Connection Establishment");
        output.ShouldContain("RPC Call");
    }

    [Fact]
    public void PrintSummary_WithMetrics_DoesNotThrow()
    {
        // Arrange
        var context = NewContext(out var writer);

        context.RequestSizeBytes = 1024;
        context.ResponseSizeBytes = 2048;
        context.MessageCount = 5;

        // Act
        context.StartPhase("Test");

        // Assert
        Should.NotThrow(() => context.PrintSummary());

        var output = writer.ToString();

        output.ShouldContain("Request Size");
        output.ShouldContain("Response Size");
        output.ShouldContain("Message Count");
    }

    [Fact]
    public void PrintSummary_WithZeroMetrics_DoesNotThrow()
    {
        // Arrange
        var context = NewContext(out _);

        context.RequestSizeBytes = 0;
        context.ResponseSizeBytes = 0;

        // Act
        context.MessageCount = 0;

        // Assert
        Should.NotThrow(() => context.PrintSummary());
    }

    [Fact]
    public void PrintSummary_CalledTwice_DoesNotThrow()
    {
        // Arrange
        var context = NewContext(out _);

        // Act
        context.StartPhase("Phase 1");

        // Assert
        Should.NotThrow(() =>
        {
            context.PrintSummary();
            context.PrintSummary();
        });
    }

    [Fact]
    public void PrintSummary_WithLargeByteValues_DoesNotThrow()
    {
        // Arrange
        var context = NewContext(out _);

        context.RequestSizeBytes = 10 * 1024 * 1024;

        // Act
        context.ResponseSizeBytes = 5 * 1024;

        // Assert
        Should.NotThrow(() => context.PrintSummary());
    }

    #endregion

    #region Integration Tests

    [Fact]
    public void FullWorkflow_PhasesPlusMetrics_DoesNotThrow()
    {
        // Arrange

        // Act
        var context = NewContext(out _);

        // Assert
        Should.NotThrow(() =>
        {
            context.StartPhase("Connection Establishment");
            context.StartPhase("Descriptor Resolution");
            context.StartPhase("RPC Invocation");
            context.RequestSizeBytes = 256;
            context.ResponseSizeBytes = 1024;
            context.MessageCount = 1;
            context.PrintSummary();
        });
    }

    [Fact]
    public void FullWorkflow_OnlyRequestSize_DoesNotThrow()
    {
        // Arrange

        // Act
        var context = NewContext(out _);

        // Assert
        Should.NotThrow(() =>
        {
            context.RequestSizeBytes = 512;
            context.PrintSummary();
        });
    }

    [Fact]
    public void FullWorkflow_OnlyResponseSize_DoesNotThrow()
    {
        // Arrange

        // Act
        var context = NewContext(out _);

        // Assert
        Should.NotThrow(() =>
        {
            context.ResponseSizeBytes = 2048;
            context.PrintSummary();
        });
    }

    [Fact]
    public void FullWorkflow_OnlyMessageCount_DoesNotThrow()
    {
        // Arrange

        // Act
        var context = NewContext(out _);

        // Assert
        Should.NotThrow(() =>
        {
            context.MessageCount = 3;
            context.PrintSummary();
        });
    }

    #endregion
}
