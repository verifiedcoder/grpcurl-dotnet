using GrpCurl.Net.TestServer.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace GrpCurl.Net.Tests.Integration.Fixtures;

[CollectionDefinition("GrpcServer", DisableParallelization = true)]
public class GrpcServerCollection : ICollectionFixture<GrpcTestFixture>;

// ReSharper disable once ClassNeverInstantiated.Global as instantiated by xUnit
public sealed class GrpcTestFixture : IAsyncLifetime
{
    private WebApplication? _app;

    private int Port { get; set; }

    public string Address => $"localhost:{Port}";

    public async ValueTask InitializeAsync()
    {
        // Find an available port
        Port = GetAvailablePort();

        var builder = WebApplication.CreateBuilder();

        // Add gRPC services
        _ = builder.Services.AddGrpc();
        _ = builder.Services.AddGrpcReflection();

        // Configure logging to suppress noise
        _ = builder.Logging.SetMinimumLevel(LogLevel.Warning);

        // Configure Kestrel to use HTTP/2 without TLS for testing
        _ = builder.WebHost.ConfigureKestrel(options =>
        {
            options.ListenLocalhost(Port, listenOptions =>
            {
                listenOptions.Protocols = HttpProtocols.Http2;
            });
        });

        _app = builder.Build();

        // Map gRPC services
        _ = _app.MapGrpcService<TestServiceImpl>();
        _ = _app.MapGrpcReflectionService();

        // Start the server
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
        // Use a listener on port 0 to get an available port
        using var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);

        listener.Start();

        var port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;

        listener.Stop();

        return port;
    }
}
