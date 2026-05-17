using System.Text.Json;
using GrpCurl.Net.Commands;
using GrpCurl.Net.Tests.Integration.Fixtures;

namespace GrpCurl.Net.Tests.Integration.Commands;

/// <summary>
///     Verifies that the test server honours the four interop fields called out in
///     CODE-REVIEW.md P1 "Test server doesn't model interop behaviour".
/// </summary>
[Collection("GrpcServer")]
public sealed class InvokeInteropFieldsTests(GrpcTestFixture fixture)
{
    [Fact]
    public async Task UnaryCall_ResponseSize_FillsPayloadToRequestedSize()
    {
        var stdout = new StringWriter();
        var originalOut = Console.Out;

        Console.SetOut(stdout);

        try
        {
            await InvokeCommandHandler.ExecuteAsync(
                address: fixture.Address,
                methodName: "testing.TestService/UnaryCall",
                data: "{\"responseSize\": 512}",
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
                connectTimeout: "5s",
                maxMsgSz: null,
                maxTime: "5s",
                authority: null,
                serverName: null,
                userAgent: null,
                allowUnknownFields: false,
                reflectHeaders: null,
                rpcHeaders: null,
                protosetOut: null,
                output: OutputFormat.Json);
        }
        finally
        {
            Console.SetOut(originalOut);
        }

        using var doc = JsonDocument.Parse(stdout.ToString().Trim());
        var payload = doc.RootElement.GetProperty("message").GetProperty("payload");
        var body = payload.GetProperty("body").GetString();

        body.ShouldNotBeNull();
        Convert.FromBase64String(body!).Length.ShouldBe(512);
    }

    [Fact]
    public async Task UnaryCall_FillUsername_PopulatesFromHeader()
    {
        var stdout = new StringWriter();
        var originalOut = Console.Out;

        Console.SetOut(stdout);

        try
        {
            await InvokeCommandHandler.ExecuteAsync(
                address: fixture.Address,
                methodName: "testing.TestService/UnaryCall",
                data: "{\"fillUsername\": true}",
                protosets: null,
                plaintext: true,
                insecure: false,
                cacert: null,
                cert: null,
                key: null,
                certPassword: null,
                headerStrings: ["x-test-username: alice"],
                verbose: false,
                veryVerbose: false,
                emitDefaults: false,
                connectTimeout: "5s",
                maxMsgSz: null,
                maxTime: "5s",
                authority: null,
                serverName: null,
                userAgent: null,
                allowUnknownFields: false,
                reflectHeaders: null,
                rpcHeaders: null,
                protosetOut: null,
                output: OutputFormat.Json);
        }
        finally
        {
            Console.SetOut(originalOut);
        }

        using var doc = JsonDocument.Parse(stdout.ToString().Trim());
        doc.RootElement.GetProperty("message").GetProperty("username").GetString().ShouldBe("alice");
    }

    [Fact]
    public async Task UnaryCall_ResponseStatus_FailsWithGivenCode()
    {
        var stderr = new StringWriter();
        var originalError = Console.Error;

        Console.SetError(stderr);

        try
        {
            var ex = await Should.ThrowAsync<Exceptions.GrpcCommandException>(async () =>
                await InvokeCommandHandler.ExecuteAsync(
                    address: fixture.Address,
                    methodName: "testing.TestService/UnaryCall",
                    data: "{\"responseStatus\": {\"code\": 5, \"message\": \"intentional\"}}",
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
                    connectTimeout: "5s",
                    maxMsgSz: null,
                    maxTime: "5s",
                    authority: null,
                    serverName: null,
                    userAgent: null,
                    allowUnknownFields: false,
                    reflectHeaders: null,
                    rpcHeaders: null,
                    protosetOut: null));

            ex.ExitCode.ShouldBe(64 + 5); // 5 = NotFound
        }
        finally
        {
            Console.SetError(originalError);
        }
    }
}
