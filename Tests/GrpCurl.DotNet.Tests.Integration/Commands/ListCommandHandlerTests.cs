using GrpCurl.Net.Commands;
using GrpCurl.Net.Exceptions;
using GrpCurl.Net.Tests.Integration.Fixtures;

namespace GrpCurl.Net.Tests.Integration.Commands;

[Collection("GrpcServer")]
public sealed class ListCommandHandlerTests(GrpcTestFixture fixture)
{
    private static readonly string TestProtosetPath = Path.Combine(AppContext.BaseDirectory, "TestProtosets", "test.protoset");

    #region Happy Path Tests

    [Fact]
    public async Task ExecuteAsync_ListAllServices_OutputsServices()
    {
        // Arrange
        var originalOut = Console.Out;

        await using var writer = new StringWriter();

        try
        {
            Console.SetOut(writer);

            // Act
            await ListCommandHandler.ExecuteAsync(
                address: fixture.Address,
                service: null,
                protosets: [],
                plaintext: true,
                insecure: false,
                cacert: null,
                cert: null,
                key: null,
                certPassword: null,
                connectTimeout: null,
                authority: null,
                serverName: null,
                verbose: false,
                veryVerbose: false,
                userAgent: null,
                headers: [],
                reflectHeaders: [],
                protosetOut: null);
        }
        finally
        {
            Console.SetOut(originalOut);
        }

        // Assert
        var output = writer.ToString();

        output.ShouldContain("testing.TestService");
        output.ShouldContain("testing.UnimplementedService");
    }

    [Fact]
    public async Task ExecuteAsync_ListMethods_OutputsMethodNames()
    {
        // Arrange
        var originalOut = Console.Out;

        await using var writer = new StringWriter();

        try
        {
            Console.SetOut(writer);

            // Act
            await ListCommandHandler.ExecuteAsync(
                address: fixture.Address,
                service: "testing.TestService",
                protosets: [],
                plaintext: true,
                insecure: false,
                cacert: null,
                cert: null,
                key: null,
                certPassword: null,
                connectTimeout: null,
                authority: null,
                serverName: null,
                verbose: false,
                veryVerbose: false,
                userAgent: null,
                headers: [],
                reflectHeaders: [],
                protosetOut: null);
        }
        finally
        {
            Console.SetOut(originalOut);
        }

        // Assert
        var output = writer.ToString();
        var lines = output.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);

        lines.Length.ShouldBe(6);

        output.ShouldContain("testing.TestService.EmptyCall");
        output.ShouldContain("testing.TestService.UnaryCall");
        output.ShouldContain("testing.TestService.StreamingOutputCall");
        output.ShouldContain("testing.TestService.StreamingInputCall");
        output.ShouldContain("testing.TestService.FullDuplexCall");
        output.ShouldContain("testing.TestService.HalfDuplexCall");

        // Methods should be sorted alphabetically
        lines[0].ShouldContain("EmptyCall");
        lines[1].ShouldContain("FullDuplexCall");
        lines[2].ShouldContain("HalfDuplexCall");
        lines[3].ShouldContain("StreamingInputCall");
        lines[4].ShouldContain("StreamingOutputCall");
        lines[5].ShouldContain("UnaryCall");
    }

    [Fact]
    public async Task ExecuteAsync_ListWithProtoset_UsesProtosetSource()
    {
        // Arrange
        var originalOut = Console.Out;

        await using var writer = new StringWriter();

        try
        {
            Console.SetOut(writer);

            // Act
            await ListCommandHandler.ExecuteAsync(
                address: null,
                service: null,
                protosets: [TestProtosetPath],
                plaintext: false,
                insecure: false,
                cacert: null,
                cert: null,
                key: null,
                certPassword: null,
                connectTimeout: null,
                authority: null,
                serverName: null,
                verbose: false,
                veryVerbose: false,
                userAgent: null,
                headers: [],
                reflectHeaders: [],
                protosetOut: null);
        }
        finally
        {
            Console.SetOut(originalOut);
        }

        // Assert
        var output = writer.ToString();

        output.ShouldNotBeNullOrWhiteSpace();
        output.ShouldContain("testing.TestService");
    }

    [Fact]
    public async Task ExecuteAsync_ListMethodsWithProtoset_UsesProtosetSource()
    {
        // Arrange
        var originalOut = Console.Out;

        await using var writer = new StringWriter();

        try
        {
            Console.SetOut(writer);

            // Act - when protoset is used, the address positional arg is treated as service
            await ListCommandHandler.ExecuteAsync(
                address: "testing.TestService",
                service: null,
                protosets: [TestProtosetPath],
                plaintext: false,
                insecure: false,
                cacert: null,
                cert: null,
                key: null,
                certPassword: null,
                connectTimeout: null,
                authority: null,
                serverName: null,
                verbose: false,
                veryVerbose: false,
                userAgent: null,
                headers: [],
                reflectHeaders: [],
                protosetOut: null);
        }
        finally
        {
            Console.SetOut(originalOut);
        }

        // Assert
        var output = writer.ToString();

        output.ShouldContain("testing.TestService.EmptyCall");
        output.ShouldContain("testing.TestService.UnaryCall");
    }

    [Fact]
    public async Task ExecuteAsync_ListWithProtosetOut_ExportsProtoset()
    {
        // Arrange
        var tempFile = Path.Combine(Path.GetTempPath(), $"list-export-{Guid.NewGuid()}.protoset");

        try
        {
            // Act
            await ListCommandHandler.ExecuteAsync(
                address: fixture.Address,
                service: null,
                protosets: [],
                plaintext: true,
                insecure: false,
                cacert: null,
                cert: null,
                key: null,
                certPassword: null,
                connectTimeout: null,
                authority: null,
                serverName: null,
                verbose: false,
                veryVerbose: false,
                userAgent: null,
                headers: [],
                reflectHeaders: [],
                protosetOut: tempFile);

            // Assert
            File.Exists(tempFile).ShouldBeTrue();

            new FileInfo(tempFile).Length.ShouldBeGreaterThan(0);
        }
        finally
        {
            if (File.Exists(tempFile))
            {
                File.Delete(tempFile);
            }
        }
    }

    #endregion

    #region Error Path Tests

    [Fact]
    public async Task ExecuteAsync_NoAddressNoProtoset_ThrowsGrpcCommandException()
    {
        // Arrange

        // Act
        // Assert
        var exception = await Should.ThrowAsync<GrpcCommandException>(() =>
            ListCommandHandler.ExecuteAsync(
                address: null,
                service: null,
                protosets: [],
                plaintext: false,
                insecure: false,
                cacert: null,
                cert: null,
                key: null,
                certPassword: null,
                connectTimeout: null,
                authority: null,
                serverName: null,
                verbose: false,
                veryVerbose: false,
                userAgent: null,
                headers: [],
                reflectHeaders: [],
                protosetOut: null));

        exception.ExitCode.ShouldBeGreaterThan(0);
    }

    [Fact]
    public async Task ExecuteAsync_InvalidService_ThrowsGrpcCommandException()
    {
        // Arrange

        // Act
        // Assert
        var exception = await Should.ThrowAsync<GrpcCommandException>(() =>
            ListCommandHandler.ExecuteAsync(
                address: fixture.Address,
                service: "testing.NonExistentService",
                protosets: [],
                plaintext: true,
                insecure: false,
                cacert: null,
                cert: null,
                key: null,
                certPassword: null,
                connectTimeout: null,
                authority: null,
                serverName: null,
                verbose: false,
                veryVerbose: false,
                userAgent: null,
                headers: [],
                reflectHeaders: [],
                protosetOut: null));

        exception.ExitCode.ShouldBeGreaterThan(0);
    }

    [Fact]
    public async Task ExecuteAsync_InvalidProtosetFile_ThrowsException()
    {
        // Arrange
        var nonExistentPath = Path.Combine(Path.GetTempPath(), "does-not-exist.protoset");

        // Act
        // Assert
        await Should.ThrowAsync<GrpcCommandException>(() =>
            ListCommandHandler.ExecuteAsync(
                address: null,
                service: null,
                protosets: [nonExistentPath],
                plaintext: false,
                insecure: false,
                cacert: null,
                cert: null,
                key: null,
                certPassword: null,
                connectTimeout: null,
                authority: null,
                serverName: null,
                verbose: false,
                veryVerbose: false,
                userAgent: null,
                headers: [],
                reflectHeaders: [],
                protosetOut: null));
    }

    [Fact]
    public async Task ExecuteAsync_AddressReassignment_WhenProtosetAndAddressButNoService()
    {
        // Arrange - When protoset is provided with an address but no service,
        // the address is reassigned to become the service argument.
        var originalOut = Console.Out;

        await using var writer = new StringWriter();

        try
        {
            Console.SetOut(writer);

            // Act - pass "testing.TestService" as address with a protoset and no service
            await ListCommandHandler.ExecuteAsync(
                address: "testing.TestService",
                service: null,
                protosets: [TestProtosetPath],
                plaintext: false,
                insecure: false,
                cacert: null,
                cert: null,
                key: null,
                certPassword: null,
                connectTimeout: null,
                authority: null,
                serverName: null,
                verbose: false,
                veryVerbose: false,
                userAgent: null,
                headers: [],
                reflectHeaders: [],
                protosetOut: null);
        }
        finally
        {
            Console.SetOut(originalOut);
        }

        // Assert - output should contain method names, proving address was treated as service
        var output = writer.ToString();
        var lines = output.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);

        lines.ShouldNotBeEmpty();

        output.ShouldContain("testing.TestService.");
    }

    #endregion
}
