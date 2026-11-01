using Google.Protobuf;
using Google.Protobuf.Reflection;
using Google.Protobuf.WellKnownTypes;
using GrpCurl.Net.Commands;
using GrpCurl.Net.Studio.Services;
using GrpCurl.Net.Studio.ViewModels.Models.Connections;
using GrpCurl.Net.Studio.ViewModels.Models.Invocation;
using GrpCurl.Net.Studio.ViewModels.Services;
using GrpCurl.Net.TestSupport;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.Logging;
using System.Buffers.Binary;
using System.Diagnostics;
using System.Net;

namespace GrpCurl.Net.Studio.Tests.Unit;

public sealed class MessageNestingIntegrationTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task Studio_reports_depth_failure_and_remains_usable(bool any, bool streaming)
    {
        await using var server = await Server.Start(any);
        var runner = new InvocationRunner(new InvocationService());
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        if (streaming)
        {
            server.ParkStream = true;
            string? diagnostic = null;
            var received = 0;
            var terminalRows = 0;
            await foreach (var row in runner.InvokeStreamingAsync(new StreamRequestModel(server.Connection, "nest.Probe/Stream", []), Requests(), timeout.Token))
            {
                if (row.Kind == StreamEventKind.MessageReceived) { received++; }
                if (row.Kind == StreamEventKind.Status)
                {
                    terminalRows++;
                    row.Status!.Code.ShouldBe((int)Grpc.Core.StatusCode.Internal);
                    _ = row.Error.ShouldNotBeNull();
                    diagnostic = row.Status.Detail;
                }
            }
            terminalRows.ShouldBe(1);
            received.ShouldBe(0);
            diagnostic.ShouldNotBeNull().ShouldContain("limit of 100");
            await server.StreamReleased.Task.WaitAsync(timeout.Token);
        }
        else
        {
            var result = await runner.InvokeUnaryAsync(new InvocationRequestModel(server.Connection, "nest.Probe/Unary", "{}", []), timeout.Token);
            result.Ok.ShouldBeFalse();
            result.Status.Code.ShouldBe((int)Grpc.Core.StatusCode.Internal);
            result.ResponseJson.ShouldBeNull();
            result.ErrorMessage.ShouldNotBeNull().ShouldContain("limit of 100");
        }
        server.Payload = NestingData.Deep(2, false);
        var good = await runner.InvokeUnaryAsync(new InvocationRequestModel(server.Connection, "nest.Probe/Unary", "{}", []), timeout.Token);
        good.Ok.ShouldBeTrue();
        good.ResponseJson.ShouldNotBeNull().ShouldContain("child");
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task CLI_exits_unsuccessfully_with_diagnostic_and_no_partial_response(bool any, bool streaming)
    {
        await using var server = await Server.Start(any);
        var start = new ProcessStartInfo("dotnet") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        foreach (var arg in new[] { typeof(InvokeCommandHandler).Assembly.Location, "invoke", "--plaintext", "--protoset", server.Protoset, "--max-time", "10s", "-d", "{}", server.Connection.Address, "nest.Probe/" + (streaming ? "Stream" : "Unary") })
        {
            start.ArgumentList.Add(arg);
        }
        using var process = Process.Start(start)!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        try { await process.WaitForExitAsync(timeout.Token); }
        finally { if (!process.HasExited) { process.Kill(entireProcessTree: true); } }
        process.ExitCode.ShouldNotBe(0);
        var output = await stdout;
        string.Join(" ", (output + await stderr).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).ShouldContain("limit of 100");
        output.ShouldNotContain("\"child\"");
        output.ShouldNotContain("\"@type\"");
    }

    private static async IAsyncEnumerable<string> Requests()
    {
        yield return "{}";
        await Task.CompletedTask;
    }

    private sealed class Server(WebApplication app, string protoset, byte[] payload) : IAsyncDisposable
    {
        public byte[] Payload { get; set; } = payload;
        public bool ParkStream { get; set; }
        public TaskCompletionSource StreamReleased { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public string Protoset { get; } = protoset;
        public SavedConnection Connection => new()
        {
            Address = app.Urls.Single().Replace("http://", "", StringComparison.Ordinal), Transport = TransportMode.Plaintext,
            DescriptorSource = new DescriptorSourceConfig { Mode = DescriptorMode.Protoset, ProtosetPaths = [Protoset] }
        };
        public static async Task<Server> Start(bool any)
        {
            var builder = WebApplication.CreateBuilder();
            _ = builder.Logging.SetMinimumLevel(LogLevel.Warning);
            _ = builder.WebHost.ConfigureKestrel(o => o.Listen(IPAddress.Loopback, 0, l => l.Protocols = HttpProtocols.Http2));
            var app = builder.Build();
            var set = new FileDescriptorSet();
            set.File.Add(Any.Descriptor.File.ToProto());
            set.File.Add(Value.Descriptor.File.ToProto());
            set.File.Add(NestingData.Descriptor.File.ToProto());
            var path = Path.Combine(Path.GetTempPath(), "grpcn-nesting-" + Guid.NewGuid().ToString("N") + ".protoset");
            await File.WriteAllBytesAsync(path, set.ToByteArray());
            var server = new Server(app, path, NestingData.Deep(120, any));
            _ = app.MapPost("/nest.Probe/{method}", async context =>
            {
                await context.Request.Body.CopyToAsync(Stream.Null, context.RequestAborted);
                context.Response.ContentType = "application/grpc";
                context.Response.DeclareTrailer("grpc-status");
                var header = new byte[5];
                BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(1), server.Payload.Length);
                await context.Response.Body.WriteAsync(header, context.RequestAborted);
                await context.Response.Body.WriteAsync(server.Payload, context.RequestAborted);
                await context.Response.Body.FlushAsync(context.RequestAborted);
                if (server.ParkStream && context.Request.Path.Value!.EndsWith("/Stream", StringComparison.Ordinal))
                {
                    try { await Task.Delay(Timeout.Infinite, context.RequestAborted); }
                    catch (OperationCanceledException) { _ = server.StreamReleased.TrySetResult(); }
                }
                else { context.Response.AppendTrailer("grpc-status", "0"); }
            });
            await app.StartAsync();
            return server;
        }
        public async ValueTask DisposeAsync()
        {
            await app.StopAsync();
            await app.DisposeAsync();
            File.Delete(Protoset);
        }
    }
}
