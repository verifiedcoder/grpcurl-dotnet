using GrpCurl.Net.Commands;
using GrpCurl.Net.Exceptions;
using GrpCurl.Net.Tests.Integration.Fixtures;

namespace GrpCurl.Net.Tests.Integration.Commands;

/// <summary>
///     Regression tests for the P0 finding in CODE-REVIEW.md: invoke must use one
///     channel for both reflection and the actual RPC, with TLS/mTLS material applied
///     uniformly. Before the fix the second channel dropped --cacert/--cert/--key so
///     mTLS-required servers rejected the business RPC even though reflection probed
///     fine. These tests would have failed before the fix and must pass after.
/// </summary>
[Collection("MTlsGrpcServer")]
public sealed class InvokeMTlsTests(MTlsGrpcTestFixture fixture)
{
    [Fact]
    public async Task InvokeWithCaAndClientCert_ReflectionAndRpcBothSucceed()
    {
        var output = new StringWriter();
        var originalOut = Console.Out;

        Console.SetOut(output);

        try
        {
            await InvokeCommandHandler.ExecuteAsync(
                address: fixture.Address,
                methodName: "testing.TestService/EmptyCall",
                data: "{}",
                protosets: null,
                plaintext: false,
                insecure: false,
                cacert: fixture.CaCertPath,
                cert: fixture.ClientCertPath,
                key: fixture.ClientKeyPath,
                certPassword: null,
                headerStrings: null,
                verbose: false,
                veryVerbose: false,
                emitDefaults: false,
                connectTimeout: "10s",
                maxMsgSz: null,
                maxTime: "10s",
                authority: null,
                serverName: "localhost",
                userAgent: null,
                allowUnknownFields: false,
                reflectHeaders: null,
                rpcHeaders: null,
                protosetOut: null,
                revocationMode: "nocheck");

            var captured = output.ToString();

            captured.ShouldContain("{");
        }
        finally
        {
            Console.SetOut(originalOut);
        }
    }

    [Fact]
    public async Task InvokeWithoutClientCert_RejectedByServer()
    {
        var output = new StringWriter();
        var originalOut = Console.Out;

        Console.SetOut(output);

        try
        {
            var act = async () => await InvokeCommandHandler.ExecuteAsync(
                address: fixture.Address,
                methodName: "testing.TestService/EmptyCall",
                data: "{}",
                protosets: null,
                plaintext: false,
                insecure: false,
                cacert: fixture.CaCertPath,
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
                serverName: "localhost",
                userAgent: null,
                allowUnknownFields: false,
                reflectHeaders: null,
                rpcHeaders: null,
                protosetOut: null,
                revocationMode: "nocheck");

            // Either a GrpcCommandException (rendered error envelope) or a raw
            // network exception is acceptable — the mTLS handshake must fail.
            await act.ShouldThrowAsync<Exception>();
        }
        finally
        {
            Console.SetOut(originalOut);
        }
    }

    [Fact]
    public async Task InvokeWithProtosetAndClientCert_RpcStillUsesClientCert()
    {
        // The schema comes from a protoset (no reflection traffic), but the RPC
        // channel must still present the client certificate. Before the P0 fix,
        // channelOptions2 dropped the cert material and this would have failed.
        var output = new StringWriter();
        var originalOut = Console.Out;

        Console.SetOut(output);

        try
        {
            var protosetPath = Path.Combine(
                Path.GetDirectoryName(typeof(InvokeMTlsTests).Assembly.Location)!,
                "TestProtosets",
                "test.protoset");

            await InvokeCommandHandler.ExecuteAsync(
                address: fixture.Address,
                methodName: "testing.TestService/EmptyCall",
                data: "{}",
                protosets: [protosetPath],
                plaintext: false,
                insecure: false,
                cacert: fixture.CaCertPath,
                cert: fixture.ClientCertPath,
                key: fixture.ClientKeyPath,
                certPassword: null,
                headerStrings: null,
                verbose: false,
                veryVerbose: false,
                emitDefaults: false,
                connectTimeout: "10s",
                maxMsgSz: null,
                maxTime: "10s",
                authority: null,
                serverName: "localhost",
                userAgent: null,
                allowUnknownFields: false,
                reflectHeaders: null,
                rpcHeaders: null,
                protosetOut: null,
                revocationMode: "nocheck");

            var captured = output.ToString();

            captured.ShouldContain("{");
        }
        finally
        {
            Console.SetOut(originalOut);
        }
    }
}
