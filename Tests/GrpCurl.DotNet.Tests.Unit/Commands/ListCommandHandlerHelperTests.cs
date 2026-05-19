using GrpCurl.Net.Commands;
using GrpCurl.Net.DescriptorSources;
using GrpCurl.Net.Exceptions;
using GrpCurl.Net.Tests.Unit.Fixtures;

namespace GrpCurl.Net.Tests.Unit.Commands;

[Collection(ConsoleStreamCollection.Name)]
public sealed class ListCommandHandlerHelperTests
{
    #region ListServicesAsync Tests

    [Fact]
    public async Task ListServicesAsync_WithServices_OutputsOnePerLine()
    {
        // Arrange
        var protosetPath = Path.Combine(
            Path.GetDirectoryName(typeof(TestDescriptorProvider).Assembly.Location)!,
            "TestProtosets",
            "test.protoset");

        var source = await ProtosetSource.LoadFromFilesAsync([protosetPath], TestContext.Current.CancellationToken);

        var originalOut = Console.Out;

        await using var writer = new StringWriter();

        Console.SetOut(writer);

        try
        {
            // Act
            await ListCommandHandler.ListServicesAsync(
                source,
                verbose: false,
                cancellationToken: TestContext.Current.CancellationToken);

            // Assert
            var output = writer.ToString();

            output.ShouldContain("testing.TestService");
            output.ShouldContain("testing.UnimplementedService");

            // Verify each service is on its own line
            var lines = output.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);

            lines.Length.ShouldBeGreaterThanOrEqualTo(2);
        }
        finally
        {
            Console.SetOut(originalOut);
        }
    }

    #endregion

    #region ListMethodsAsync Tests

    [Fact]
    public async Task ListMethodsAsync_ValidService_OutputsMethodNames()
    {
        // Arrange
        var protosetPath = Path.Combine(
            Path.GetDirectoryName(typeof(TestDescriptorProvider).Assembly.Location)!,
            "TestProtosets",
            "test.protoset");

        var source = await ProtosetSource.LoadFromFilesAsync([protosetPath], TestContext.Current.CancellationToken);

        var originalOut = Console.Out;

        await using var writer = new StringWriter();

        Console.SetOut(writer);

        try
        {
            // Act
            await ListCommandHandler.ListMethodsAsync(
                source,
                "testing.TestService",
                verbose: false,
                cancellationToken: TestContext.Current.CancellationToken);

            // Assert
            var output = writer.ToString();

            output.ShouldContain("testing.TestService.EmptyCall");
            output.ShouldContain("testing.TestService.UnaryCall");
            output.ShouldContain("testing.TestService.StreamingOutputCall");
            output.ShouldContain("testing.TestService.StreamingInputCall");
            output.ShouldContain("testing.TestService.FullDuplexCall");
            output.ShouldContain("testing.TestService.HalfDuplexCall");
        }
        finally
        {
            Console.SetOut(originalOut);
        }
    }

    [Fact]
    public async Task ListMethodsAsync_InvalidService_ThrowsGrpcCommandException()
    {
        // Arrange
        var protosetPath = Path.Combine(
            Path.GetDirectoryName(typeof(TestDescriptorProvider).Assembly.Location)!,
            "TestProtosets",
            "test.protoset");

        var source = await ProtosetSource.LoadFromFilesAsync([protosetPath], TestContext.Current.CancellationToken);

        // Act
        // Assert
        var exception = await Should.ThrowAsync<GrpcCommandException>(
            () => ListCommandHandler.ListMethodsAsync(
                source,
                "testing.SimpleRequest",
                verbose: false,
                cancellationToken: TestContext.Current.CancellationToken));

        exception.ShouldNotBeNull();
    }

    #endregion
}
