using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using GrpCurl.Net.TestServer.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.AspNetCore.Server.Kestrel.Https;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace GrpCurl.Net.Studio.Tests.Unit.Fixtures;

/// <summary>
///     Hosts the shared gRPC <see cref="TestServiceImpl" /> over HTTPS with
///     <see cref="ClientCertificateMode.RequireCertificate" />, validating client certs against the
///     checked-in <c>ca.crt</c>. The Studio analogue of the CLI's mTLS fixture: it lets an L2 test
///     drive the GUI service layer through a TLS profile (custom CA + client cert/key) end-to-end.
///     Mirrors <see cref="StudioPlaintextServerFixture" /> (no console redirection) so Studio
///     integration collections may run in parallel.
/// </summary>
[CollectionDefinition(Name)]
public sealed class StudioMTlsServerCollection : ICollectionFixture<StudioMTlsServerFixture>
{
    public const string Name = "StudioMTlsServer";
}

public sealed class StudioMTlsServerFixture : IAsyncLifetime
{
    private WebApplication? _app;
    private X509Certificate2? _trustedClientCa;

    public string Address { get; private set; } = string.Empty;

    public string CaCertPath { get; private set; } = string.Empty;

    public string ClientCertPath { get; private set; } = string.Empty;

    public string ClientKeyPath { get; private set; } = string.Empty;

    public string WrongCaCertPath { get; private set; } = string.Empty;

    public async ValueTask InitializeAsync()
    {
        var certRoot = ResolveCertRoot();

        CaCertPath = Path.Combine(certRoot, "ca.crt");
        ClientCertPath = Path.Combine(certRoot, "client.crt");
        ClientKeyPath = Path.Combine(certRoot, "client.key");
        WrongCaCertPath = Path.Combine(certRoot, "wrong-ca.crt");

        var serverCert = LoadPemCertificateWithKey(
            Path.Combine(certRoot, "server.crt"),
            Path.Combine(certRoot, "server.key"));

        _trustedClientCa = X509CertificateLoader.LoadCertificateFromFile(CaCertPath);

        var port = GetAvailablePort();
        Address = $"localhost:{port}";

        var builder = WebApplication.CreateBuilder();

        builder.Services.AddGrpc();
        builder.Services.AddGrpcReflection();
        builder.Logging.SetMinimumLevel(LogLevel.Warning);

        builder.WebHost.ConfigureKestrel(options =>
            options.ListenLocalhost(port, listenOptions =>
            {
                listenOptions.Protocols = HttpProtocols.Http2;
                listenOptions.UseHttps(httpsOptions =>
                {
                    httpsOptions.ServerCertificate = serverCert;
                    httpsOptions.ClientCertificateMode = ClientCertificateMode.RequireCertificate;
                    httpsOptions.AllowAnyClientCertificate();
                    httpsOptions.ClientCertificateValidation = (cert, _, _) => ValidateClient(cert, _trustedClientCa);
                });
            }));

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

        _trustedClientCa?.Dispose();
    }

    private static string ResolveCertRoot()
    {
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "TestCertificates"),
            Path.Combine(Environment.CurrentDirectory, "TestCertificates")
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
            "Test certificate fixture not found. Expected TestCertificates/server.crt under the bin output.");
    }

    private static bool ValidateClient(X509Certificate2 clientCertificate, X509Certificate2? trustedCa)
    {
        if (trustedCa is null)
        {
            return false;
        }

        using var chain = new X509Chain();

        chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
        chain.ChainPolicy.CustomTrustStore.Add(trustedCa);
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
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
}
