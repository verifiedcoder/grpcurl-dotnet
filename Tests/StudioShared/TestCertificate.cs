using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace GrpCurl.Net.Studio.TestSupport;

/// <summary>Generates throwaway self-signed PEM certificates for cert-inspection tests (FR-037).</summary>
public static class TestCertificate
{
    /// <summary>Creates a self-signed certificate and writes it as PEM to a fresh temp file; returns the path.</summary>
    public static string WritePem(string subjectName = "CN=grpcn-test", int notBeforeDays = -1, int notAfterDays = 365)
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest(subjectName, rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var cert = request.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddDays(notBeforeDays),
            DateTimeOffset.UtcNow.AddDays(notAfterDays));

        var path = Path.Combine(Path.GetTempPath(), $"grpcn-cert-{Guid.NewGuid():N}.pem");
        File.WriteAllText(path, cert.ExportCertificatePem());
        return path;
    }

    /// <summary>The SHA-256 fingerprint (uppercase, colon-separated) of the PEM certificate at <paramref name="path" />.</summary>
    public static string Sha256Of(string path)
    {
        using var cert = X509Certificate2.CreateFromPem(File.ReadAllText(path));
        var hex = Convert.ToHexString(SHA256.HashData(cert.RawData));
        return string.Join(':', Enumerable.Range(0, hex.Length / 2).Select(i => hex.Substring(i * 2, 2)));
    }
}
