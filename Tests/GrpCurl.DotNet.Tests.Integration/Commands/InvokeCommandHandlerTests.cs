using GrpCurl.Net.Commands;
using GrpCurl.Net.Exceptions;
using GrpCurl.Net.Tests.Integration.Fixtures;

namespace GrpCurl.Net.Tests.Integration.Commands;

[Collection("GrpcServer")]
public sealed class InvokeCommandHandlerTests(GrpcTestFixture fixture)
{
    private string Address => fixture.Address;

    private static string TestProtosetPath => Path.Combine(AppContext.BaseDirectory, "TestProtosets", "test.protoset");

    #region Happy Path Tests

    [Fact]
    public async Task ExecuteAsync_UnaryEmptyCall_OutputsJsonResponse()
    {
        // Arrange
        var output = new StringWriter();
        var originalOut = Console.Out;

        Console.SetOut(output);

        try
        {
            // Act
            await InvokeCommandHandler.ExecuteAsync(
                address: Address,
                methodName: "testing.TestService/EmptyCall",
                data: "{}",
                protosets: null,
                plaintext: true,
                insecure: false,
                cacert: null,
                cert: null,
                key: null,
                certPassword: null,
                headerStrings: null,
                verbose: false,
                veryVerbose: false,
                emitDefaults: false,
                connectTimeout: null,
                maxMsgSz: null,
                maxTime: null,
                authority: null,
                serverName: null,
                userAgent: null,
                allowUnknownFields: false,
                reflectHeaders: null,
                rpcHeaders: null,
                protosetOut: null);

            // Assert
            var result = output.ToString().Trim();

            result.ShouldNotBeNullOrWhiteSpace();
            result.ShouldContain("{");
            result.ShouldContain("}");
        }
        finally
        {
            Console.SetOut(originalOut);
        }
    }

    [Fact]
    public async Task ExecuteAsync_UnaryCallWithPayload_OutputsResponse()
    {
        // Arrange
        var output = new StringWriter();
        var originalOut = Console.Out;

        Console.SetOut(output);

        const string requestData = """
            {
                "payload": {
                    "body": "dGVzdA=="
                }
            }
            """;

        try
        {
            // Act
            await InvokeCommandHandler.ExecuteAsync(
                address: Address,
                methodName: "testing.TestService/UnaryCall",
                data: requestData,
                protosets: null,
                plaintext: true,
                insecure: false,
                cacert: null,
                cert: null,
                key: null,
                certPassword: null,
                headerStrings: null,
                verbose: false,
                veryVerbose: false,
                emitDefaults: false,
                connectTimeout: null,
                maxMsgSz: null,
                maxTime: null,
                authority: null,
                serverName: null,
                userAgent: null,
                allowUnknownFields: false,
                reflectHeaders: null,
                rpcHeaders: null,
                protosetOut: null);

            // Assert
            var result = output.ToString().Trim();

            result.ShouldNotBeNullOrWhiteSpace();
            result.ShouldContain("payload");
            result.ShouldContain("body");
        }
        finally
        {
            Console.SetOut(originalOut);
        }
    }

    [Fact]
    public async Task ExecuteAsync_ServerStreaming_OutputsMultipleResponses()
    {
        // Arrange
        var output = new StringWriter();
        var originalOut = Console.Out;

        Console.SetOut(output);

        const string requestData = """
            {
                "response_parameters": [
                    { "size": 10 },
                    { "size": 20 },
                    { "size": 30 }
                ]
            }
            """;

        try
        {
            // Act
            await InvokeCommandHandler.ExecuteAsync(
                address: Address,
                methodName: "testing.TestService/StreamingOutputCall",
                data: requestData,
                protosets: null,
                plaintext: true,
                insecure: false,
                cacert: null,
                cert: null,
                key: null,
                certPassword: null,
                headerStrings: null,
                verbose: false,
                veryVerbose: false,
                emitDefaults: false,
                connectTimeout: null,
                maxMsgSz: null,
                maxTime: null,
                authority: null,
                serverName: null,
                userAgent: null,
                allowUnknownFields: false,
                reflectHeaders: null,
                rpcHeaders: null,
                protosetOut: null);

            // Assert - should contain multiple JSON objects with payload
            var result = output.ToString();
            var payloadCount = CountOccurrences(result, "\"payload\"");

            payloadCount.ShouldBe(3);
        }
        finally
        {
            Console.SetOut(originalOut);
        }
    }

    [Fact]
    public async Task ExecuteAsync_UnaryCallWithEmitDefaults_IncludesDefaultFields()
    {
        // Arrange
        var output = new StringWriter();
        var originalOut = Console.Out;

        Console.SetOut(output);

        try
        {
            // Act
            await InvokeCommandHandler.ExecuteAsync(
                address: Address,
                methodName: "testing.TestService/UnaryCall",
                data: "{}",
                protosets: null,
                plaintext: true,
                insecure: false,
                cacert: null,
                cert: null,
                key: null,
                certPassword: null,
                headerStrings: null,
                verbose: false,
                veryVerbose: false,
                emitDefaults: true,
                connectTimeout: null,
                maxMsgSz: null,
                maxTime: null,
                authority: null,
                serverName: null,
                userAgent: null,
                allowUnknownFields: false,
                reflectHeaders: null,
                rpcHeaders: null,
                protosetOut: null);

            // Assert - with emitDefaults, default-valued fields should appear in the output
            var result = output.ToString();

            result.ShouldContain("username");
        }
        finally
        {
            Console.SetOut(originalOut);
        }
    }

    [Fact]
    public async Task ExecuteAsync_UnaryCallWithProtoset_UsesProtosetSource()
    {
        // Arrange
        var output = new StringWriter();
        var originalOut = Console.Out;

        Console.SetOut(output);

        try
        {
            // Act
            await InvokeCommandHandler.ExecuteAsync(
                address: Address,
                methodName: "testing.TestService/EmptyCall",
                data: "{}",
                protosets: [TestProtosetPath],
                plaintext: true,
                insecure: false,
                cacert: null,
                cert: null,
                key: null,
                certPassword: null,
                headerStrings: null,
                verbose: false,
                veryVerbose: false,
                emitDefaults: false,
                connectTimeout: null,
                maxMsgSz: null,
                maxTime: null,
                authority: null,
                serverName: null,
                userAgent: null,
                allowUnknownFields: false,
                reflectHeaders: null,
                rpcHeaders: null,
                protosetOut: null);

            // Assert - should succeed and produce valid JSON output
            var result = output.ToString().Trim();

            result.ShouldNotBeNullOrWhiteSpace();
            result.ShouldContain("{");
            result.ShouldContain("}");
        }
        finally
        {
            Console.SetOut(originalOut);
        }
    }

    [Fact]
    public async Task ExecuteAsync_UnaryCallWithProtosetOut_ExportsProtoset()
    {
        // Arrange
        var output = new StringWriter();
        var originalOut = Console.Out;

        Console.SetOut(output);

        var tempFile = Path.Combine(Path.GetTempPath(), $"protoset-out-{Guid.NewGuid()}.protoset");

        try
        {
            // Act
            await InvokeCommandHandler.ExecuteAsync(
                address: Address,
                methodName: "testing.TestService/EmptyCall",
                data: "{}",
                protosets: null,
                plaintext: true,
                insecure: false,
                cacert: null,
                cert: null,
                key: null,
                certPassword: null,
                headerStrings: null,
                verbose: false,
                veryVerbose: false,
                emitDefaults: false,
                connectTimeout: null,
                maxMsgSz: null,
                maxTime: null,
                authority: null,
                serverName: null,
                userAgent: null,
                allowUnknownFields: false,
                reflectHeaders: null,
                rpcHeaders: null,
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
    public async Task ExecuteAsync_InvalidMethodFormat_ThrowsGrpcCommandException()
    {
        // Arrange
        var output = new StringWriter();
        var originalOut = Console.Out;

        Console.SetOut(output);

        try
        {
            // Act
            // Assert
            var exception = await Should.ThrowAsync<GrpcCommandException>(() =>
                InvokeCommandHandler.ExecuteAsync(
                    address: Address,
                    methodName: "BadFormat",
                    data: "{}",
                    protosets: null,
                    plaintext: true,
                    insecure: false,
                    cacert: null,
                    cert: null,
                    key: null,
                    certPassword: null,
                    headerStrings: null,
                    verbose: false,
                    veryVerbose: false,
                    emitDefaults: false,
                    connectTimeout: null,
                    maxMsgSz: null,
                    maxTime: null,
                    authority: null,
                    serverName: null,
                    userAgent: null,
                    allowUnknownFields: false,
                    reflectHeaders: null,
                    rpcHeaders: null,
                    protosetOut: null));

            exception.ShouldNotBeNull();
        }
        finally
        {
            Console.SetOut(originalOut);
        }
    }

    [Fact]
    public async Task ExecuteAsync_UnknownService_ThrowsGrpcCommandException()
    {
        // Arrange
        var output = new StringWriter();
        var originalOut = Console.Out;

        Console.SetOut(output);

        try
        {
            // Act
            // Assert
            var exception = await Should.ThrowAsync<GrpcCommandException>(() =>
                InvokeCommandHandler.ExecuteAsync(
                    address: Address,
                    methodName: "fake.Service/Method",
                    data: "{}",
                    protosets: null,
                    plaintext: true,
                    insecure: false,
                    cacert: null,
                    cert: null,
                    key: null,
                    certPassword: null,
                    headerStrings: null,
                    verbose: false,
                    veryVerbose: false,
                    emitDefaults: false,
                    connectTimeout: null,
                    maxMsgSz: null,
                    maxTime: null,
                    authority: null,
                    serverName: null,
                    userAgent: null,
                    allowUnknownFields: false,
                    reflectHeaders: null,
                    rpcHeaders: null,
                    protosetOut: null));

            exception.ShouldNotBeNull();
        }
        finally
        {
            Console.SetOut(originalOut);
        }
    }

    [Fact]
    public async Task ExecuteAsync_UnknownMethod_ThrowsGrpcCommandException()
    {
        // Arrange
        var output = new StringWriter();
        var originalOut = Console.Out;

        Console.SetOut(output);

        try
        {
            // Act
            // Assert
            var exception = await Should.ThrowAsync<GrpcCommandException>(() =>
                InvokeCommandHandler.ExecuteAsync(
                    address: Address,
                    methodName: "testing.TestService/FakeMethod",
                    data: "{}",
                    protosets: null,
                    plaintext: true,
                    insecure: false,
                    cacert: null,
                    cert: null,
                    key: null,
                    certPassword: null,
                    headerStrings: null,
                    verbose: false,
                    veryVerbose: false,
                    emitDefaults: false,
                    connectTimeout: null,
                    maxMsgSz: null,
                    maxTime: null,
                    authority: null,
                    serverName: null,
                    userAgent: null,
                    allowUnknownFields: false,
                    reflectHeaders: null,
                    rpcHeaders: null,
                    protosetOut: null));

            exception.ShouldNotBeNull();
        }
        finally
        {
            Console.SetOut(originalOut);
        }
    }

    [Fact]
    public async Task ExecuteAsync_InvalidJson_ThrowsGrpcCommandException()
    {
        // Arrange
        var output = new StringWriter();
        var originalOut = Console.Out;

        Console.SetOut(output);

        try
        {
            // Act
            // Assert
            var exception = await Should.ThrowAsync<GrpcCommandException>(() =>
                InvokeCommandHandler.ExecuteAsync(
                    address: Address,
                    methodName: "testing.TestService/EmptyCall",
                    data: "{bad json",
                    protosets: null,
                    plaintext: true,
                    insecure: false,
                    cacert: null,
                    cert: null,
                    key: null,
                    certPassword: null,
                    headerStrings: null,
                    verbose: false,
                    veryVerbose: false,
                    emitDefaults: false,
                    connectTimeout: null,
                    maxMsgSz: null,
                    maxTime: null,
                    authority: null,
                    serverName: null,
                    userAgent: null,
                    allowUnknownFields: false,
                    reflectHeaders: null,
                    rpcHeaders: null,
                    protosetOut: null));

            exception.ShouldNotBeNull();
        }
        finally
        {
            Console.SetOut(originalOut);
        }
    }

    [Fact]
    public async Task ExecuteAsync_InvalidProtosetFile_ThrowsException()
    {
        // Arrange
        var output = new StringWriter();
        var originalOut = Console.Out;

        Console.SetOut(output);

        try
        {
            // Act
            // Assert
            // (wrapping FileNotFoundException)
            var exception = await Should.ThrowAsync<GrpcCommandException>(() =>
                InvokeCommandHandler.ExecuteAsync(
                    address: Address,
                    methodName: "testing.TestService/EmptyCall",
                    data: "{}",
                    protosets: ["/nonexistent/path/bad.protoset"],
                    plaintext: true,
                    insecure: false,
                    cacert: null,
                    cert: null,
                    key: null,
                    certPassword: null,
                    headerStrings: null,
                    verbose: false,
                    veryVerbose: false,
                    emitDefaults: false,
                    connectTimeout: null,
                    maxMsgSz: null,
                    maxTime: null,
                    authority: null,
                    serverName: null,
                    userAgent: null,
                    allowUnknownFields: false,
                    reflectHeaders: null,
                    rpcHeaders: null,
                    protosetOut: null));

            exception.ShouldNotBeNull();
        }
        finally
        {
            Console.SetOut(originalOut);
        }
    }

    [Fact]
    public async Task ExecuteAsync_OutputJson_OnRpcError_EmitsErrorEnvelopeOnStderr()
    {
        // Arrange
        // Use the fail-early metadata header to trigger a server-side RpcException (StatusCode 13 = Internal).
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        var originalOut = Console.Out;
        var originalErr = Console.Error;

        Console.SetOut(stdout);
        Console.SetError(stderr);

        // Act
        try

        // Assert
        {
            var exception = await Should.ThrowAsync<GrpcCommandException>(() =>
                InvokeCommandHandler.ExecuteAsync(
                    address: Address,
                    methodName: "testing.TestService/EmptyCall",
                    data: "{}",
                    protosets: null,
                    plaintext: true,
                    insecure: false,
                    cacert: null,
                    cert: null,
                    key: null,
                    certPassword: null,
                    headerStrings: ["fail-early: 13"],
                    verbose: false,
                    veryVerbose: false,
                    emitDefaults: false,
                    connectTimeout: null,
                    maxMsgSz: null,
                    maxTime: null,
                    authority: null,
                    serverName: null,
                    userAgent: null,
                    allowUnknownFields: false,
                    reflectHeaders: null,
                    rpcHeaders: null,
                    protosetOut: null,
                    output: OutputFormat.Json));

            exception.ShouldNotBeNull();
            exception.ExitCode.ShouldBe(64 + 13);

            stdout.ToString().ShouldBeEmpty();

            var line = stderr.ToString().Trim();

            line.ShouldStartWith("{");
            line.ShouldEndWith("}");

            using var doc = System.Text.Json.JsonDocument.Parse(line);
            var root = doc.RootElement;

            root.GetProperty("kind").GetString().ShouldBe("error");
            root.GetProperty("category").GetString().ShouldBe("rpc");
            root.GetProperty("exitCode").GetInt32().ShouldBe(64 + 13);

            var grpc = root.GetProperty("grpc");

            grpc.GetProperty("code").GetInt32().ShouldBe(13);
            grpc.GetProperty("status").GetString().ShouldBe("Internal");
        }
        finally
        {
            Console.SetOut(originalOut);
            Console.SetError(originalErr);
        }
    }

    #endregion

    #region Helpers

    private static int CountOccurrences(string source, string substring)
    {
        var count = 0;
        var index = 0;

        while ((index = source.IndexOf(substring, index, StringComparison.Ordinal)) != -1)
        {
            count++;
            index += substring.Length;
        }

        return count;
    }

    #endregion
}
