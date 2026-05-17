using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using GrpCurl.Net.TestServer.Services;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.AspNetCore.Server.Kestrel.Https;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddGrpc();
builder.Services.AddGrpcReflection();
builder.Logging.SetMinimumLevel(LogLevel.Warning);

var port = 9090;
var useTls = false;
string? tlsCertPath = null;
string? tlsKeyPath = null;
string? tlsCertPassword = null;
string? clientCaPath = null;
var requireClientCert = false;

for (var i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--port" or "-p" when i + 1 < args.Length:

            if (int.TryParse(args[i + 1], out var p))
            {
                port = p;
            }

            i++;
            break;

        case "--tls":

            useTls = true;
            break;

        case "--tls-cert" when i + 1 < args.Length:

            useTls = true;
            tlsCertPath = args[++i];
            break;

        case "--tls-key" when i + 1 < args.Length:

            tlsKeyPath = args[++i];
            break;

        case "--tls-password" when i + 1 < args.Length:

            tlsCertPassword = args[++i];
            break;

        case "--require-client-cert":

            requireClientCert = true;
            useTls = true;
            break;

        case "--client-ca" when i + 1 < args.Length:

            clientCaPath = args[++i];
            requireClientCert = true;
            useTls = true;
            break;
    }
}

if (useTls)
{
    var certPath = tlsCertPath ?? ResolveCertPath("server.crt");
    var keyPath = tlsKeyPath ?? ResolveCertPath("server.key");
    var serverCert = LoadServerCertificate(certPath, keyPath, tlsCertPassword);
    var trustedClientCa = clientCaPath is null && requireClientCert
        ? X509CertificateLoader.LoadCertificateFromFile(ResolveCertPath("ca.crt"))
        : clientCaPath is not null
            ? X509CertificateLoader.LoadCertificateFromFile(clientCaPath)
            : null;

    builder.WebHost.ConfigureKestrel(options =>
    {
        options.ListenLocalhost(port, listenOptions =>
        {
            listenOptions.Protocols = HttpProtocols.Http2;
            listenOptions.UseHttps(httpsOptions =>
            {
                httpsOptions.ServerCertificate = serverCert;

                if (requireClientCert)
                {
                    httpsOptions.ClientCertificateMode = ClientCertificateMode.RequireCertificate;
                    httpsOptions.AllowAnyClientCertificate();
                    httpsOptions.ClientCertificateValidation = (cert, chain, errors) =>
                        ValidateClientCertificate(cert, chain, errors, trustedClientCa);
                }
            });
        });
    });
}
else
{
    builder.WebHost.ConfigureKestrel(options =>
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
    X509Certificate2 clientCertificate,
    X509Chain? chain,
    SslPolicyErrors errors,
    X509Certificate2? trustedCa)
{
    if (trustedCa is null)
    {
        // No CA configured: accept any cert. Useful for local development only.
        return true;
    }

    using var validationChain = new X509Chain();

    validationChain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
    validationChain.ChainPolicy.CustomTrustStore.Add(trustedCa);
    validationChain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
    validationChain.ChainPolicy.VerificationFlags = X509VerificationFlags.NoFlag;

    return validationChain.Build(clientCertificate);
}
