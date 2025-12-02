using GrpCurl.Net.Exceptions;

namespace GrpCurl.Net.Tests.Unit.Exceptions;

public sealed class GrpcCommandExceptionTests
{
    #region Constructor Tests

    [Fact]
    public void Constructor_WithMessageOnly_SetsDefaultExitCode()
    {
        // Arrange & Act
        var exception = new GrpcCommandException("Test error");

        // Assert
        exception.Message.ShouldBe("Test error");
        exception.ExitCode.ShouldBe(1);
    }

    [Fact]
    public void Constructor_WithMessageAndExitCode_SetsProperties()
    {
        // Arrange & Act
        var exception = new GrpcCommandException("Custom error", 42);

        // Assert
        exception.Message.ShouldBe("Custom error");
        exception.ExitCode.ShouldBe(42);
    }

    [Fact]
    public void Constructor_WithZeroExitCode_SetsExitCode()
    {
        // Arrange & Act
        var exception = new GrpcCommandException("Success with error", 0);

        // Assert
        exception.ExitCode.ShouldBe(0);
    }

    [Fact]
    public void Constructor_WithNegativeExitCode_SetsExitCode()
    {
        // Arrange & Act
        var exception = new GrpcCommandException("Negative code", -1);

        // Assert
        exception.ExitCode.ShouldBe(-1);
    }

    [Fact]
    public void Constructor_WithLargeExitCode_SetsExitCode()
    {
        // Arrange & Act
        var exception = new GrpcCommandException("Large code", 255);

        // Assert
        exception.ExitCode.ShouldBe(255);
    }

    [Fact]
    public void Constructor_WithEmptyMessage_SetsMessage()
    {
        // Arrange & Act
        var exception = new GrpcCommandException("");

        // Assert
        exception.Message.ShouldBe("");
        exception.ExitCode.ShouldBe(1);
    }

    #endregion

    #region Inheritance Tests

    [Fact]
    public void GrpcCommandException_InheritsFromException()
    {
        // Arrange & Act
        var exception = new GrpcCommandException("Test");

        // Assert
        exception.ShouldBeAssignableTo<Exception>();
    }

    [Fact]
    public void GrpcCommandException_CanBeCaught_AsException()
    {
        // Arrange
        Exception? caught;

        // Act
        try
        {
            throw new GrpcCommandException("Test error", 5);
        }
        catch (Exception ex)
        {
            caught = ex;
        }

        // Assert
        caught.ShouldNotBeNull();
        var grpcException = caught.ShouldBeOfType<GrpcCommandException>();
        grpcException.ExitCode.ShouldBe(5);
    }

    #endregion

    #region Exit Code Convention Tests

    [Theory]
    [InlineData(0, "Success")]
    [InlineData(1, "General error")]
    [InlineData(64, "RPC base code")]
    [InlineData(130, "User cancellation (Ctrl+C)")]
    public void ExitCode_CommonValues_AreSupported(int exitCode, string description)
    {
        // Arrange & Act
        var exception = new GrpcCommandException(description, exitCode);

        // Assert
        exception.ExitCode.ShouldBe(exitCode);
    }

    [Fact]
    public void ExitCode_RpcStatusCodes_CanBeEncoded()
    {
        // Arrange
        // RPC exit codes are typically 64 + StatusCode
        // StatusCode.Cancelled = 1, so exit code = 65
        const int cancelledExitCode = 64 + 1;

        // Act
        var exception = new GrpcCommandException("RPC cancelled", cancelledExitCode);

        // Assert
        exception.ExitCode.ShouldBe(65);
    }

    #endregion
}
