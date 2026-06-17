using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace GrpCurl.Net.Studio.ViewModels.Connections;

/// <summary>
///     FR-037: human-readable facts parsed from a certificate file — subject, issuer, validity window,
///     and the SHA-256 fingerprint. A display aid only; Core stays the validation authority at call time.
/// </summary>
public sealed record CertificateFacts(
    string Subject,
    string Issuer,
    DateTimeOffset NotBefore,
    DateTimeOffset NotAfter,
    string Sha256Fingerprint)
{
    public bool IsExpired => DateTimeOffset.Now > NotAfter;

    public bool IsNotYetValid => DateTimeOffset.Now < NotBefore;

    /// <summary>FR-037: flags an out-of-window certificate so the editor can warn without blocking.</summary>
    public bool IsOutOfValidity => IsExpired || IsNotYetValid;

    public string ValidityText => $"{NotBefore:yyyy-MM-dd} → {NotAfter:yyyy-MM-dd}"
        + (IsExpired ? " (expired)" : IsNotYetValid ? " (not yet valid)" : string.Empty);
}

/// <summary>Reads <see cref="CertificateFacts" /> from a PEM or DER certificate file; never throws.</summary>
public static class CertificateInspector
{
    /// <summary>
    ///     Parses the leaf certificate from <paramref name="path" />. Returns <see langword="null" /> when the
    ///     file is absent, isn't a readable X.509 certificate, or is a password-protected PKCS#12 bundle
    ///     (whose contents can't be inspected without the password) — all non-fatal in the editor.
    /// </summary>
    public static CertificateFacts? TryRead(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return null;
        }

        try
        {
            using var cert = Load(path);
            return cert is null ? null : Describe(cert);
        }
        catch (CryptographicException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static X509Certificate2? Load(string path)
    {
        var bytes = File.ReadAllBytes(path);

        // A PEM file is ASCII text carrying a -----BEGIN CERTIFICATE----- armor block.
        var head = Encoding.ASCII.GetString(bytes, 0, Math.Min(bytes.Length, 64));

        if (head.Contains("-----BEGIN", StringComparison.Ordinal))
        {
            var text = Encoding.ASCII.GetString(bytes);
            return text.Contains("CERTIFICATE", StringComparison.Ordinal)
                ? X509Certificate2.CreateFromPem(text)
                : null; // a key-only PEM (no certificate to inspect)
        }

        // Otherwise treat it as a single DER-encoded certificate. PKCS#12 bundles fall through to the
        // CryptographicException catch above (they need a password), which is the intended no-op.
        return X509CertificateLoader.LoadCertificate(bytes);
    }

    private static CertificateFacts Describe(X509Certificate2 cert)
    {
        var fingerprint = Convert.ToHexString(SHA256.HashData(cert.RawData));
        var colonised = string.Join(':', Enumerable.Range(0, fingerprint.Length / 2)
            .Select(i => fingerprint.Substring(i * 2, 2)));

        return new CertificateFacts(
            cert.Subject,
            cert.Issuer,
            cert.NotBefore.ToUniversalTime(),
            cert.NotAfter.ToUniversalTime(),
            colonised);
    }
}
