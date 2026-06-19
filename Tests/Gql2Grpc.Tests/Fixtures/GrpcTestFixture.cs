using GrpCurl.Net.TestServer.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Gql2Grpc.Tests.Fixtures;

[CollectionDefinition("GrpcServer")]
public class GrpcServerCollection : ICollectionFixture<GrpcTestFixture>;

public sealed class GrpcTestFixture : IAsyncLifetime
{
    private WebApplication? _app;

    public int Port { get; private set; }

    public string Address => $"localhost:{Port}";

    public async ValueTask InitializeAsync()
    {
        Port = GetAvailablePort();

        var builder = WebApplication.CreateBuilder();

        _ = builder.Services.AddGrpc();
        _ = builder.Services.AddGrpcReflection();
        _ = builder.Logging.SetMinimumLevel(LogLevel.Warning);

        _ = builder.WebHost.ConfigureKestrel(options =>
        {
            options.ListenLocalhost(Port, listenOptions =>
            {
                listenOptions.Protocols = HttpProtocols.Http2;
            });
        });

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

        GC.SuppressFinalize(this);
    }

    private static int GetAvailablePort()
    {
        using var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);

        listener.Start();

        var port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;

        listener.Stop();

        return port;
    }
}
