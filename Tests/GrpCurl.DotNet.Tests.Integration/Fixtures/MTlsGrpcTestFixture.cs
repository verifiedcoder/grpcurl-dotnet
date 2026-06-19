using GrpCurl.Net.TestServer.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.AspNetCore.Server.Kestrel.Https;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Security.Cryptography.X509Certificates;

namespace GrpCurl.Net.Tests.Integration.Fixtures;

[CollectionDefinition("MTlsGrpcServer", DisableParallelization = true)]
public class MTlsGrpcServerCollection : ICollectionFixture<MTlsGrpcTestFixture>;

/// <summary>
///     Starts an in-process gRPC test server bound to HTTPS with the checked-in test
///     certificate fixture and <see cref="ClientCertificateMode.RequireCertificate"/>.
///     Used by tests that exercise the P0 fix: reflection and RPC must both use the
///     same TLS/mTLS-equipped channel. The server presents <c>server.crt</c>/<c>server.key</c>
///     and validates client certs against <c>ca.crt</c>.
/// </summary>
// ReSharper disable once ClassNeverInstantiated.Global
public sealed class MTlsGrpcTestFixture : IAsyncLifetime
{
    private WebApplication? _app;
    private X509Certificate2? _trustedClientCa;

    public int Port { get; private set; }

    public string Address => $"localhost:{Port}";

    public string CaCertPath { get; private set; } = string.Empty;

    public string ServerCertPath { get; private set; } = string.Empty;

    public string ClientCertPath { get; private set; } = string.Empty;

    public string ClientKeyPath { get; private set; } = string.Empty;

    public string WrongCaCertPath { get; private set; } = string.Empty;

    public async ValueTask InitializeAsync()
    {
        var certRoot = ResolveCertRoot();

        CaCertPath = Path.Combine(certRoot, "ca.crt");
        ServerCertPath = Path.Combine(certRoot, "server.crt");
        ClientCertPath = Path.Combine(certRoot, "client.crt");
        ClientKeyPath = Path.Combine(certRoot, "client.key");
        WrongCaCertPath = Path.Combine(certRoot, "wrong-ca.crt");

        var serverCert = LoadPemCertificateWithKey(
            ServerCertPath,
            Path.Combine(certRoot, "server.key"));

        _trustedClientCa = X509CertificateLoader.LoadCertificateFromFile(CaCertPath);

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
                _ = listenOptions.UseHttps(httpsOptions =>
                {
                    httpsOptions.ServerCertificate = serverCert;
                    httpsOptions.ClientCertificateMode = ClientCertificateMode.RequireCertificate;
                    httpsOptions.AllowAnyClientCertificate();
                    httpsOptions.ClientCertificateValidation = (cert, _, _) =>
                        ValidateClient(cert, _trustedClientCa);
                });
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

        _trustedClientCa?.Dispose();
    }

    private static string ResolveCertRoot()
    {
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "TestCertificates"),
            Path.Combine(Environment.CurrentDirectory, "TestCertificates"),
            Path.Combine(Environment.CurrentDirectory, "..", "TestCertificates"),
            Path.Combine(Environment.CurrentDirectory, "..", "..", "TestCertificates")
        };

        foreach (var candidate in candidates)
        {
            var full = Path.GetFullPath(candidate);

            if (File.Exists(Path.Combine(full, "server.crt")))
            {
                return full;
            }
        }

        throw new FileNotFoundException(
            "Test certificate fixture not found. Expected TestCertificates/server.crt under the bin output. " +
            "Ensure the test project's <Content Include=\"..\\TestCertificates\\**\\*\"> rule is copying them.");
    }

    private static bool ValidateClient(X509Certificate2 clientCertificate, X509Certificate2? trustedCa)
    {
        if (trustedCa is null)
        {
            return false;
        }

        using var chain = new X509Chain();

        chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
        _ = chain.ChainPolicy.CustomTrustStore.Add(trustedCa);
        chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;

        return chain.Build(clientCertificate);
    }

    private static X509Certificate2 LoadPemCertificateWithKey(string certPath, string keyPath)
    {
        using var pemCert = X509Certificate2.CreateFromPemFile(certPath, keyPath);

        return X509CertificateLoader.LoadPkcs12(
            pemCert.Export(X509ContentType.Pkcs12),
            password: null,
            X509KeyStorageFlags.Exportable);
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
