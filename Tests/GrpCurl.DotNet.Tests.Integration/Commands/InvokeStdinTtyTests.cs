using GrpCurl.Net.Commands;
using GrpCurl.Net.Exceptions;
using GrpCurl.Net.Tests.Integration.Fixtures;
using GrpCurl.Net.Utilities;

namespace GrpCurl.Net.Tests.Integration.Commands;

[Collection("GrpcServer")]
public sealed class InvokeStdinTtyTests(GrpcTestFixture fixture)
{
    private string Address => fixture.Address;

    [Fact]
    public async Task ExecuteAsync_DataAt_StdinNotRedirected_FailsFastWithUsageError()
    {
        ConsoleEnvironment.SetIsInputRedirectedOverride(() => false);

        try
        {
            var ex = await Should.ThrowAsync<GrpcCommandException>(() =>
                InvokeCommandHandler.ExecuteAsync(
                    address: Address,
                    methodName: "testing.TestService/StreamingInputCall",
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
                    maxTime: null,
                    authority: null,
                    serverName: null,
                    userAgent: null,
                    allowUnknownFields: false,
                    reflectHeaders: null,
                    rpcHeaders: null,
                    protosetOut: null,
                    output: OutputFormat.Json));

            ex.ExitCode.ShouldBe(2);
            ex.Envelope.ShouldNotBeNull();
            ex.Envelope.Category.ShouldBe(ErrorCategory.Usage);
            ex.Envelope.Message.ShouldContain("--data @ requires stdin to be redirected");
        }
        finally
        {
            ConsoleEnvironment.SetIsInputRedirectedOverride(null);
        }
    }
}
