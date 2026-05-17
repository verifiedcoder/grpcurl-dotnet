using GrpCurl.Net.Commands;
using GrpCurl.Net.Exceptions;
using GrpCurl.Net.Tests.Integration.Fixtures;

namespace GrpCurl.Net.Tests.Integration.Commands;

[Collection("GrpcServer")]
public sealed class DescribeCommandHandlerTests(GrpcTestFixture fixture)
{
    private static readonly string TestProtosetPath = Path.Combine(AppContext.BaseDirectory, "TestProtosets", "test.protoset");

    #region Happy Path Tests

    [Fact]
    public async Task ExecuteAsync_DescribeService_OutputsServiceDefinition()
    {
        // Arrange
        var writer = new StringWriter();
        var originalOut = Console.Out;
        
        Console.SetOut(writer);

        try
        {
            // Act
            await DescribeCommandHandler.ExecuteAsync(
                address: fixture.Address,
                symbol: "testing.TestService",
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
                msgTemplate: false,
                protosetOut: null);

            // Assert
            var output = writer.ToString();
            
            output.ShouldContain("testing.TestService is a service");
            output.ShouldContain("EmptyCall");
            output.ShouldContain("UnaryCall");
            output.ShouldContain("StreamingOutputCall");
            output.ShouldContain("StreamingInputCall");
            output.ShouldContain("FullDuplexCall");
            output.ShouldContain("HalfDuplexCall");
        }
        finally
        {
            Console.SetOut(originalOut);
        }
    }

    [Fact]
    public async Task ExecuteAsync_DescribeMessage_OutputsMessageDefinition()
    {
        // Arrange
        var writer = new StringWriter();
        var originalOut = Console.Out;
        
        Console.SetOut(writer);

        try
        {
            // Act
            await DescribeCommandHandler.ExecuteAsync(
                address: fixture.Address,
                symbol: "testing.SimpleRequest",
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
                msgTemplate: false,
                protosetOut: null);

            // Assert
            var output = writer.ToString();
            output.ShouldContain("testing.SimpleRequest is a message");
            output.ShouldContain("message SimpleRequest");
            output.ShouldContain("response_type");
            output.ShouldContain("response_size");
            output.ShouldContain("payload");
        }
        finally
        {
            Console.SetOut(originalOut);
        }
    }

    [Fact]
    public async Task ExecuteAsync_DescribeEnum_OutputsEnumDefinition()
    {
        // Arrange
        var writer = new StringWriter();
        var originalOut = Console.Out;
        
        Console.SetOut(writer);

        try
        {
            // Act
            await DescribeCommandHandler.ExecuteAsync(
                address: fixture.Address,
                symbol: "testing.PayloadType",
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
                msgTemplate: false,
                protosetOut: null);

            // Assert
            var output = writer.ToString();
            
            output.ShouldContain("testing.PayloadType is an enum");
            output.ShouldContain("enum PayloadType");
            output.ShouldContain("COMPRESSABLE");
        }
        finally
        {
            Console.SetOut(originalOut);
        }
    }

    [Fact]
    public async Task ExecuteAsync_DescribeAllServices_ListsServices()
    {
        // Arrange
        var writer = new StringWriter();
        var originalOut = Console.Out;
        
        Console.SetOut(writer);

        try
        {
            // Act
            await DescribeCommandHandler.ExecuteAsync(
                address: fixture.Address,
                symbol: null,
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
                msgTemplate: false,
                protosetOut: null);

            // Assert
            var output = writer.ToString();
            
            output.ShouldContain("testing.TestService is a service");
            output.ShouldContain("testing.UnimplementedService is a service");
        }
        finally
        {
            Console.SetOut(originalOut);
        }
    }

    [Fact]
    public async Task ExecuteAsync_DescribeWithMsgTemplate_OutputsProtoDefinitionAndJsonTemplate()
    {
        // Arrange
        var writer = new StringWriter();
        var originalOut = Console.Out;

        Console.SetOut(writer);

        try
        {
            // Act
            await DescribeCommandHandler.ExecuteAsync(
                address: fixture.Address,
                symbol: "testing.SimpleRequest",
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
                msgTemplate: true,
                protosetOut: null);

            // Assert - msg-template now outputs proto definition + blank line + "Message template:" + JSON
            var output = writer.ToString();

            // Proto definition should appear first
            output.ShouldContain("message SimpleRequest {");
            output.ShouldContain("Message template:");

            // JSON template with snake_case field names
            output.ShouldContain("{");
            output.ShouldContain("}");
            output.ShouldContain("response_type");
            output.ShouldContain("response_size");
            output.ShouldContain("payload");
        }
        finally
        {
            Console.SetOut(originalOut);
        }
    }

    [Fact]
    public async Task ExecuteAsync_DescribeWithProtoset_UsesProtosetSource()
    {
        // Arrange
        var writer = new StringWriter();
        var originalOut = Console.Out;
        
        Console.SetOut(writer);

        try
        {
            // Act - when protosets are provided without a server address,
            // the address parameter is treated as the symbol
            await DescribeCommandHandler.ExecuteAsync(
                address: "testing.TestService",
                symbol: null,
                protosets: [TestProtosetPath],
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
                msgTemplate: false,
                protosetOut: null);

            // Assert
            var output = writer.ToString();
            
            output.ShouldContain("testing.TestService is a service");
        }
        finally
        {
            Console.SetOut(originalOut);
        }
    }

    [Fact]
    public async Task ExecuteAsync_DescribeWithProtosetOut_ExportsProtoset()
    {
        // Arrange
        var tempFile = Path.Combine(Path.GetTempPath(), $"describe-test-{Guid.NewGuid()}.protoset");
        var writer = new StringWriter();
        var originalOut = Console.Out;
        
        Console.SetOut(writer);

        try
        {
            // Act
            await DescribeCommandHandler.ExecuteAsync(
                address: fixture.Address,
                symbol: "testing.TestService",
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
                msgTemplate: false,
                protosetOut: tempFile);

            // Assert
            File.Exists(tempFile).ShouldBeTrue();
            
            var fileBytes = await File.ReadAllBytesAsync(tempFile, TestContext.Current.CancellationToken);
            
            fileBytes.Length.ShouldBeGreaterThan(0);
        }
        finally
        {
            Console.SetOut(originalOut);

            if (File.Exists(tempFile))
            {
                File.Delete(tempFile);
            }
        }
    }

    #endregion

    #region Error Path Tests

    [Fact]
    public async Task ExecuteAsync_UnknownSymbol_ThrowsGrpcCommandException()
    {
        // Arrange
        var writer = new StringWriter();
        var originalOut = Console.Out;
        
        Console.SetOut(writer);

        try
        {
            // Act & Assert
            await Should.ThrowAsync<GrpcCommandException>(() =>
                DescribeCommandHandler.ExecuteAsync(
                    address: fixture.Address,
                    symbol: "testing.NonExistentSymbol",
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
                    msgTemplate: false,
                    protosetOut: null));
        }
        finally
        {
            Console.SetOut(originalOut);
        }
    }

    [Fact]
    public async Task ExecuteAsync_NoAddressNoProtoset_ThrowsGrpcCommandException()
    {
        // Arrange & Act & Assert
        await Should.ThrowAsync<GrpcCommandException>(() =>
            DescribeCommandHandler.ExecuteAsync(
                address: null,
                symbol: null,
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
                msgTemplate: false,
                protosetOut: null));
    }

    [Fact]
    public async Task ExecuteAsync_InvalidProtosetFile_ThrowsException()
    {
        // Arrange
        var badPath = Path.Combine(Path.GetTempPath(), $"nonexistent-{Guid.NewGuid()}.protoset");

        // Act & Assert
        await Should.ThrowAsync<GrpcCommandException>(() =>
            DescribeCommandHandler.ExecuteAsync(
                address: null,
                symbol: "testing.TestService",
                protosets: [badPath],
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
                msgTemplate: false,
                protosetOut: null));
    }

    #endregion
}
