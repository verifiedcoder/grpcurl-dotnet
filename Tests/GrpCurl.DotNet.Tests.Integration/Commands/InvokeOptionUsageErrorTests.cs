using GrpCurl.Net.Commands;
using GrpCurl.Net.Exceptions;
using GrpCurl.Net.Tests.Integration.Fixtures;

namespace GrpCurl.Net.Tests.Integration.Commands;

/// <summary>
///     Malformed connection-option *values* (as opposed to parse-time syntax errors) must map
///     to the usage contract — category Usage, exit code 2 — not the generic internal-error
///     path (exit 1). The conversions run before any network I/O, so these fail fast against
///     the live fixture address without ever connecting.
/// </summary>
[Collection("GrpcServer")]
public sealed class InvokeOptionUsageErrorTests(GrpcTestFixture fixture)
{
    private string Address => fixture.Address;

    private Task RunAsync(
        string? connectTimeout = null,
        string? maxMsgSz = null,
        string? maxTime = null,
        string? revocationMode = null,
        string? keepaliveTime = null,
        string? keepaliveTimeout = null)
        => InvokeCommandHandler.ExecuteAsync(
            address: Address,
            methodName: "grpc.testing.TestService/UnaryCall",
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
            connectTimeout: connectTimeout,
            maxMsgSz: maxMsgSz,
            maxTime: maxTime,
            authority: null,
            serverName: null,
            userAgent: null,
            allowUnknownFields: false,
            reflectHeaders: null,
            rpcHeaders: null,
            protosetOut: null,
            revocationMode: revocationMode,
            keepaliveTime: keepaliveTime,
            keepaliveTimeout: keepaliveTimeout);

    [Fact]
    public async Task ExecuteAsync_BadMaxTime_ExitsWithUsageCode()
    {
        var output = new StringWriter();
        var originalOut = Console.Out;
        Console.SetOut(output);

        try
        {
            var ex = await Should.ThrowAsync<GrpcCommandException>(() => RunAsync(maxTime: "nope"));
            ex.ExitCode.ShouldBe(2);
        }
        finally
        {
            Console.SetOut(originalOut);
        }
    }

    [Fact]
    public async Task ExecuteAsync_BadConnectTimeout_ExitsWithUsageCode()
    {
        var output = new StringWriter();
        var originalOut = Console.Out;
        Console.SetOut(output);

        try
        {
            var ex = await Should.ThrowAsync<GrpcCommandException>(() => RunAsync(connectTimeout: "nope"));
            ex.ExitCode.ShouldBe(2);
        }
        finally
        {
            Console.SetOut(originalOut);
        }
    }

    [Fact]
    public async Task ExecuteAsync_BadMaxMsgSz_ExitsWithUsageCode()
    {
        var output = new StringWriter();
        var originalOut = Console.Out;
        Console.SetOut(output);

        try
        {
            var ex = await Should.ThrowAsync<GrpcCommandException>(() => RunAsync(maxMsgSz: "nope"));
            ex.ExitCode.ShouldBe(2);
        }
        finally
        {
            Console.SetOut(originalOut);
        }
    }

    [Fact]
    public async Task ExecuteAsync_BadRevocationMode_ExitsWithUsageCode()
    {
        var output = new StringWriter();
        var originalOut = Console.Out;
        Console.SetOut(output);

        try
        {
            var ex = await Should.ThrowAsync<GrpcCommandException>(() => RunAsync(revocationMode: "bad"));
            ex.ExitCode.ShouldBe(2);
        }
        finally
        {
            Console.SetOut(originalOut);
        }
    }

    [Fact]
    public async Task ExecuteAsync_BadKeepaliveTime_ExitsWithUsageCode()
    {
        var output = new StringWriter();
        var originalOut = Console.Out;
        Console.SetOut(output);

        try
        {
            var ex = await Should.ThrowAsync<GrpcCommandException>(() => RunAsync(keepaliveTime: "nope"));
            ex.ExitCode.ShouldBe(2);
        }
        finally
        {
            Console.SetOut(originalOut);
        }
    }
}
