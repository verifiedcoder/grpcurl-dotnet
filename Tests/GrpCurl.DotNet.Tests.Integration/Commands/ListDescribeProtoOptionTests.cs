using GrpCurl.Net.Commands;
using GrpCurl.Net.Exceptions;

namespace GrpCurl.Net.Tests.Integration.Commands;

/// <summary>
///     Covers the review finding that <c>--proto</c>/<c>-I</c> existed only on
///     <c>invoke</c>: list/describe now accept .proto source files (compiled via local
///     protoc) as a schema source, and a missing protoc yields the purpose-built install
///     guidance instead of the misleading "Protoset file not found: " message.
///     No collection fixture: these tests need no gRPC server, and the assembly already
///     disables collection parallelism (required here because PATH is mutated).
/// </summary>
public sealed class ListDescribeProtoOptionTests : IDisposable
{
    private const string ProtoContent =
        """
        syntax = "proto3";

        package demo;

        service DemoService {
          rpc Ping (PingRequest) returns (PingReply);
        }

        message PingRequest {
          string msg = 1;
        }

        message PingReply {
          string msg = 1;
        }
        """;

    private readonly string _protoPath;

    public ListDescribeProtoOptionTests()
    {
        _protoPath = Path.Combine(Path.GetTempPath(), $"grpcurl-test-{Guid.NewGuid():N}.proto");

        File.WriteAllText(_protoPath, ProtoContent);
    }

    public void Dispose()
    {
        try
        {
            File.Delete(_protoPath);
        }
        catch
        {
            // Best effort.
        }
    }

    private static bool ProtocOnPath()
    {
        var executable = OperatingSystem.IsWindows() ? "protoc.exe" : "protoc";
        var pathVar = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;

        return pathVar
            .Split(Path.PathSeparator)
            .Where(dir => !string.IsNullOrWhiteSpace(dir))
            .Any(dir => File.Exists(Path.Combine(dir, executable)));
    }

    [Fact]
    public async Task List_WithProtoOnly_ListsCompiledService()
    {
        Assert.SkipUnless(ProtocOnPath(), "protoc is not installed on PATH");

        // Arrange
        var output = new StringWriter();
        var originalOut = Console.Out;

        Console.SetOut(output);

        // Act
        try

        // Assert
        {
            await ListCommandHandler.ExecuteAsync(
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
                protosetOut: null,
                protoFiles: [_protoPath]);

            output.ToString().ShouldContain("demo.DemoService");
        }
        finally
        {
            Console.SetOut(originalOut);
        }
    }

    [Fact]
    public async Task Describe_WithProtoOnly_DescribesCompiledService()
    {
        Assert.SkipUnless(ProtocOnPath(), "protoc is not installed on PATH");

        // Arrange
        var output = new StringWriter();
        var originalOut = Console.Out;

        Console.SetOut(output);

        // Act
        try

        // Assert
        {
            await DescribeCommandHandler.ExecuteAsync(
                address: null,
                symbol: "demo.DemoService",
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
                protosetOut: null,
                protoFiles: [_protoPath]);

            output.ToString().ShouldContain("rpc Ping");
        }
        finally
        {
            Console.SetOut(originalOut);
        }
    }

    [Fact]
    public async Task List_WithProtoButNoProtoc_RendersInstallGuidance()
    {
        // Arrange — hide protoc by clearing PATH for the duration of the call.
        var originalPath = Environment.GetEnvironmentVariable("PATH");
        var output = new StringWriter();
        var originalOut = Console.Out;

        Console.SetOut(output);
        Environment.SetEnvironmentVariable("PATH", string.Empty);

        // Act
        try

        // Assert
        {
            var ex = await Should.ThrowAsync<GrpcCommandException>(() =>
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
                    protosetOut: null,
                    protoFiles: [_protoPath]));

            ex.ExitCode.ShouldBe(3);
            ex.Envelope.ShouldNotBeNull();
            ex.Envelope.Category.ShouldBe(ErrorCategory.Schema);
            ex.Envelope.Message.ShouldContain("protoc not found on PATH");
            ex.Envelope.Message.ShouldNotContain("Protoset file not found");
        }
        finally
        {
            Environment.SetEnvironmentVariable("PATH", originalPath);
            Console.SetOut(originalOut);
        }
    }

    [Fact]
    public async Task List_WithoutProtoProtosetOrAddress_FailsWithUsageError()
    {
        // Arrange
        var output = new StringWriter();
        var originalOut = Console.Out;

        Console.SetOut(output);

        // Act
        try

        // Assert
        {
            var ex = await Should.ThrowAsync<GrpcCommandException>(() =>
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

            ex.ExitCode.ShouldBe(2);
            ex.Envelope.ShouldNotBeNull();
            ex.Envelope.Category.ShouldBe(ErrorCategory.Usage);
        }
        finally
        {
            Console.SetOut(originalOut);
        }
    }
}
