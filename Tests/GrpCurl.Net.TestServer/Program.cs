using GrpCurl.Net.TestServer.Services;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.AspNetCore.Server.Kestrel.Https;
using System.Security.Cryptography.X509Certificates;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddGrpc();
builder.Services.AddGrpcReflection();
builder.Logging.SetMinimumLevel(LogLevel.Warning);

var (port, useTls, tlsCertPath, tlsKeyPath, tlsCertPassword, clientCaPath, requireClientCert) = ParseOptions(args);

if (useTls)
{
    var certPath = tlsCertPath ?? ResolveCertPath("server.crt");
    var keyPath = tlsKeyPath ?? ResolveCertPath("server.key");
    var serverCert = LoadServerCertificate(certPath, keyPath, tlsCertPassword);
    var trustedClientCa = LoadTrustedClientCa(clientCaPath, requireClientCert);

    _ = builder.WebHost.ConfigureKestrel(options =>
    {
        options.ListenLocalhost(port, listenOptions =>
        {
            listenOptions.Protocols = HttpProtocols.Http2;
            _ = listenOptions.UseHttps(httpsOptions =>
            {
                httpsOptions.ServerCertificate = serverCert;

                if (requireClientCert)
                {
                    httpsOptions.ClientCertificateMode = ClientCertificateMode.RequireCertificate;
                    httpsOptions.AllowAnyClientCertificate();
                    httpsOptions.ClientCertificateValidation = (cert, _, _) =>
                        ValidateClientCertificate(cert, trustedClientCa);
                }
            });
        });
    });
}
else
{
    _ = builder.WebHost.ConfigureKestrel(options =>
    {
        options.ListenLocalhost(port, listenOptions => listenOptions.Protocols = HttpProtocols.Http2);
    });
}

var app = builder.Build();

app.MapGrpcService<TestServiceImpl>();
app.MapGrpcReflectionService();

Console.WriteLine($"TestServer listening on port {port} (TLS: {useTls}, mTLS: {requireClientCert})");

await app.RunAsync();

return;

static string ResolveCertPath(string fileName)
{
    var probeRoots = new[]
    {
        AppContext.BaseDirectory,
        Path.Combine(AppContext.BaseDirectory, "TestCertificates"),
        Path.Combine(Environment.CurrentDirectory, "TestCertificates"),
        Path.Combine(Environment.CurrentDirectory, "..", "TestCertificates"),
        Path.Combine(Environment.CurrentDirectory, "..", "..", "TestCertificates")
    };

    foreach (var root in probeRoots)
    {
        var candidate = Path.GetFullPath(Path.Combine(root, fileName));

        if (File.Exists(candidate))
        {
            return candidate;
        }
    }

    throw new FileNotFoundException(
        $"Test certificate '{fileName}' not found. Looked in: {string.Join(", ", probeRoots)}. " +
        "Run Tests/TestCertificates/generate-certs.sh (or generate-certs.ps1) first.");
}

static X509Certificate2? LoadTrustedClientCa(string? clientCaPath, bool requireClientCert)
{
    if (clientCaPath is not null)
    {
        return X509CertificateLoader.LoadCertificateFromFile(clientCaPath);
    }

    return requireClientCert
        ? X509CertificateLoader.LoadCertificateFromFile(ResolveCertPath("ca.crt"))
        : null;
}

static X509Certificate2 LoadServerCertificate(string certPath, string keyPath, string? password)
{
    if (Path.GetExtension(certPath).Equals(".pfx", StringComparison.OrdinalIgnoreCase) ||
        Path.GetExtension(certPath).Equals(".p12", StringComparison.OrdinalIgnoreCase))
    {
        return X509CertificateLoader.LoadPkcs12FromFile(certPath, password);
    }

    using var pemCert = X509Certificate2.CreateFromPemFile(certPath, keyPath);

    return X509CertificateLoader.LoadPkcs12(
        pemCert.Export(X509ContentType.Pkcs12),
        password: null,
        X509KeyStorageFlags.Exportable);
}

static bool ValidateClientCertificate(
    X509Certificate2? clientCertificate,
    X509Certificate2? trustedCa)
{
    if (clientCertificate is null)
    {
        return false;
    }

    if (trustedCa is null)
    {
        // No CA configured: accept any cert. Useful for local development only.
        return true;
    }

    using var validationChain = new X509Chain();

    validationChain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
    _ = validationChain.ChainPolicy.CustomTrustStore.Add(trustedCa);
    validationChain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
    validationChain.ChainPolicy.VerificationFlags = X509VerificationFlags.NoFlag;

    return validationChain.Build(clientCertificate);
}

static (int Port, bool UseTls, string? TlsCertPath, string? TlsKeyPath, string? TlsCertPassword, string? ClientCaPath, bool RequireClientCert) ParseOptions(
    IEnumerable<string> commandLineArgs)
{
    var port = 9090;
    var useTls = false;
    string? tlsCertPath = null;
    string? tlsKeyPath = null;
    string? tlsCertPassword = null;
    string? clientCaPath = null;
    var requireClientCert = false;
    var remaining = new Queue<string>(commandLineArgs);

    while (remaining.TryDequeue(out var arg))
    {
        switch (arg)
        {
            case "--port" or "-p":
                if (int.TryParse(TryTakeOptionValue(remaining), out var p))
                {
                    port = p;
                }

                break;

            case "--tls":
                useTls = true;
                break;

            case "--tls-cert":
                useTls = true;
                tlsCertPath = TryTakeOptionValue(remaining);
                break;

            case "--tls-key":
                tlsKeyPath = TryTakeOptionValue(remaining);
                break;

            case "--tls-password":
                tlsCertPassword = TryTakeOptionValue(remaining);
                break;

            case "--require-client-cert":
                requireClientCert = true;
                useTls = true;
                break;

            case "--client-ca":
                clientCaPath = TryTakeOptionValue(remaining);
                requireClientCert = true;
                useTls = true;
                break;
        }
    }

    return (port, useTls, tlsCertPath, tlsKeyPath, tlsCertPassword, clientCaPath, requireClientCert);
}

static string? TryTakeOptionValue(Queue<string> remaining) =>
    remaining.TryDequeue(out var value) ? value : null;
