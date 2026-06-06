using GrpCurl.Net.Commands;

namespace GrpCurl.Net.Tests.Integration.Commands;

/// <summary>
///     Covers the review finding that <c>--proto-out-dir</c> existed only on
///     <c>invoke</c>: list/describe can now reconstruct .proto sources from the active
///     schema. Uses the checked-in protoset fixture so no server or protoc is required.
/// </summary>
public sealed class ListDescribeProtoOutDirTests : IDisposable
{
    private static readonly string TestProtosetPath = Path.Combine(AppContext.BaseDirectory, "TestProtosets", "test.protoset");

    private readonly string _outDir = Path.Combine(Path.GetTempPath(), $"grpcurl-proto-out-{Guid.NewGuid():N}");

    public void Dispose()
    {
        try
        {
            Directory.Delete(_outDir, true);
        }
        catch
        {
            // Best effort.
        }
    }

    [Fact]
    public async Task List_WithProtoOutDir_WritesReconstructedProtoFiles()
    {
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
                protosetOut: null,
                protoOutDir: _outDir);

            Directory.Exists(_outDir).ShouldBeTrue();
            Directory.GetFiles(_outDir, "*.proto", SearchOption.AllDirectories).ShouldNotBeEmpty();
        }
        finally
        {
            Console.SetOut(originalOut);
        }
    }

    [Fact]
    public async Task Describe_WithProtoOutDir_WritesReconstructedProtoFiles()
    {
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
                symbol: "testing.TestService",
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
                msgTemplate: false,
                protosetOut: null,
                protoOutDir: _outDir);

            Directory.Exists(_outDir).ShouldBeTrue();
            Directory.GetFiles(_outDir, "*.proto", SearchOption.AllDirectories).ShouldNotBeEmpty();
        }
        finally
        {
            Console.SetOut(originalOut);
        }
    }
}
