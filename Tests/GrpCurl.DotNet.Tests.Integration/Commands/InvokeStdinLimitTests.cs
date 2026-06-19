using GrpCurl.Net.Commands;
using GrpCurl.Net.Exceptions;
using GrpCurl.Net.Tests.Integration.Fixtures;
using GrpCurl.Net.Utilities;

namespace GrpCurl.Net.Tests.Integration.Commands;

/// <summary>
///     Covers the review finding that stdin exceeding <c>--max-stdin-bytes</c> exited 1
///     (internal) instead of 2 (usage): the cap is a caller-correctable input problem and
///     now renders a Usage envelope.
/// </summary>
[Collection("GrpcServer")]
public sealed class InvokeStdinLimitTests(GrpcTestFixture fixture)
{
    [Fact]
    public async Task ExecuteAsync_StdinExceedsMaxStdinBytes_FailsWithUsageError()
    {
        // Arrange — stdin carries more bytes than the configured cap.
        ConsoleEnvironment.SetIsInputRedirectedOverride(() => true);
        ConsoleEnvironment.SetStandardInputOverride(() => new MemoryStream(new byte[64]));

        // Act
        try

        // Assert
        {
            var ex = await Should.ThrowAsync<GrpcCommandException>(() =>
                InvokeCommandHandler.ExecuteAsync(
                    address: fixture.Address,
                    methodName: "testing.TestService/EmptyCall",
                    data: "@",
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
                    maxTime: "10s",
                    authority: null,
                    serverName: null,
                    userAgent: null,
                    allowUnknownFields: false,
                    reflectHeaders: null,
                    rpcHeaders: null,
                    protosetOut: null,
                    output: OutputFormat.Json,
                    maxStdinBytes: 16));

            ex.ExitCode.ShouldBe(2);
            _ = ex.Envelope.ShouldNotBeNull();
            ex.Envelope.Category.ShouldBe(ErrorCategory.Usage);
            ex.Envelope.Message.ShouldContain("exceeded the maximum allowed size");
        }
        finally
        {
            ConsoleEnvironment.SetIsInputRedirectedOverride(null);
            ConsoleEnvironment.SetStandardInputOverride(null);
        }
    }
}
