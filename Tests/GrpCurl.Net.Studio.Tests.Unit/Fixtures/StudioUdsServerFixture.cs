using GrpCurl.Net.TestServer.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace GrpCurl.Net.Studio.Tests.Unit.Fixtures;

/// <summary>
///     Hosts the shared gRPC <see cref="TestServiceImpl" /> over a Unix domain socket (plaintext h2c),
///     for the E2.5 UDS service-layer test. Linux/macOS only — Core (and gRPC) reject UDS on Windows,
///     so the consuming test skips there.
/// </summary>
[CollectionDefinition(Name)]
public sealed class StudioUdsServerCollection : ICollectionFixture<StudioUdsServerFixture>
{
    public const string Name = "StudioUdsServer";
}

public sealed class StudioUdsServerFixture : IAsyncLifetime
{
    private WebApplication? _app;

    /// <summary>The socket file path; empty on Windows (the fixture is a no-op there).</summary>
    public string SocketPath { get; private set; } = string.Empty;

    /// <summary>The connection address: <c>unix:///&lt;socket&gt;</c>.</summary>
    public string Address => $"unix://{SocketPath}";

    public async ValueTask InitializeAsync()
    {
        if (OperatingSystem.IsWindows())
        {
            return; // UDS unsupported; the test skips on Windows.
        }

        SocketPath = Path.Combine(Path.GetTempPath(), $"grpcn-uds-{Guid.NewGuid():N}.sock");

        var builder = WebApplication.CreateBuilder();
        builder.Services.AddGrpc();
        builder.Services.AddGrpcReflection();
        builder.Logging.SetMinimumLevel(LogLevel.Warning);

        builder.WebHost.ConfigureKestrel(options =>
            options.ListenUnixSocket(SocketPath, listenOptions => listenOptions.Protocols = HttpProtocols.Http2));

        _app = builder.Build();
        _app.MapGrpcService<TestServiceImpl>();
        _app.MapGrpcReflectionService();

        await _app.StartAsync();
    }

    public async ValueTask DisposeAsync()
    {
        if (_app is not null)
        {
            await _app.StopAsync();
            await _app.DisposeAsync();
        }

        if (!string.IsNullOrEmpty(SocketPath))
        {
            try { File.Delete(SocketPath); } catch (IOException) { }
        }
    }
}
