using GrpCurl.Net.TestServer.Services;
using Microsoft.AspNetCore.Server.Kestrel.Core;

var builder = WebApplication.CreateBuilder(args);

// Add gRPC services
builder.Services.AddGrpc();
builder.Services.AddGrpcReflection();

builder.Logging.SetMinimumLevel(LogLevel.Warning);

// Parse command line arguments for port configuration
var port = 9090; // Default port
var useTls = false;

for (var i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--port" or "-p" when i + 1 < args.Length:

            if (int.TryParse(args[i + 1], out var p))
            {
                port = p;
            }

            break;

        case "--tls":

            useTls = true;

            break;
    }
}

// Configure the server to listen on the specified port
if (useTls)
{
    builder.WebHost.ConfigureKestrel(options =>
    {
        options.ListenLocalhost(port, listenOptions =>
        {
            listenOptions.UseHttps();
            listenOptions.Protocols = HttpProtocols.Http2;
        });
    });
}
else
{
    builder.WebHost.ConfigureKestrel(options => { options.ListenLocalhost(port, listenOptions => { listenOptions.Protocols = HttpProtocols.Http2; }); });
}

var app = builder.Build();

// Map gRPC services
app.MapGrpcService<TestServiceImpl>();
app.MapGrpcReflectionService();

Console.WriteLine($"TestServer listening on port {port} (TLS: {useTls})");

await app.RunAsync();
