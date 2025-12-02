using GrpCurl.Net.TestServer.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace GrpCurl.Net.Tests.Integration.Fixtures;

[CollectionDefinition("GrpcServer")]
public class GrpcServerCollection : ICollectionFixture<GrpcTestFixture>;

// ReSharper disable once ClassNeverInstantiated.Global as instantiated by xUnit
public class GrpcTestFixture : IAsyncLifetime
{
    private WebApplication? _app;

    private int Port { get; set; }

    public string Address => $"localhost:{Port}";

    public async Task InitializeAsync()
    {
        // Find an available port
        Port = GetAvailablePort();

        var builder = WebApplication.CreateBuilder();

        // Add gRPC services
        builder.Services.AddGrpc();
        builder.Services.AddGrpcReflection();

        // Configure logging to suppress noise
        builder.Logging.SetMinimumLevel(LogLevel.Warning);

        // Configure Kestrel to use HTTP/2 without TLS for testing
        builder.WebHost.ConfigureKestrel(options =>
        {
            options.ListenLocalhost(Port, listenOptions =>
            {
                listenOptions.Protocols = HttpProtocols.Http2;
            });
        });

        _app = builder.Build();

        // Map gRPC services
        _app.MapGrpcService<TestServiceImpl>();
        _app.MapGrpcReflectionService();

        // Start the server
        await _app.StartAsync();
    }

    public async Task DisposeAsync()
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
