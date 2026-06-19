using GrpCurl.Net.TestServer.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Net.Sockets;

namespace GrpCurl.Net.Studio.Tests.Unit.Fixtures;

/// <summary>
///     Hosts the shared in-process gRPC <see cref="TestServiceImpl" /> over plaintext HTTP/2 on a
///     dynamically allocated loopback port, for Studio service-layer (L2) integration tests.
///     Mirrors the CLI integration fixture but does NOT redirect the console, so Studio
///     integration collections may run in parallel (SPEC-070 §1).
/// </summary>
[CollectionDefinition(Name)]
public sealed class StudioPlaintextServerCollection : ICollectionFixture<StudioPlaintextServerFixture>
{
    public const string Name = "StudioPlaintextServer";
}

public sealed class StudioPlaintextServerFixture : IAsyncLifetime
{
    private WebApplication? _app;

    public string Address { get; private set; } = string.Empty;

    public async ValueTask InitializeAsync()
    {
        var port = GetAvailablePort();
        Address = $"localhost:{port}";

        var builder = WebApplication.CreateBuilder();

        _ = builder.Services.AddGrpc();
        _ = builder.Services.AddGrpcReflection();
        _ = builder.Logging.SetMinimumLevel(LogLevel.Warning);

        _ = builder.WebHost.ConfigureKestrel(options =>
            options.ListenLocalhost(port, listenOptions => listenOptions.Protocols = HttpProtocols.Http2));

        _app = builder.Build();
        _ = _app.MapGrpcService<TestServiceImpl>();
        _ = _app.MapGrpcReflectionService();

        await _app.StartAsync();
    }

    public async ValueTask DisposeAsync()
    {
        if (_app is not null)
        {
            await _app.StopAsync();
            await _app.DisposeAsync();
        }
    }

    private static int GetAvailablePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
}
