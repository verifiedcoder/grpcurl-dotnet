using GrpCurl.Net.Studio.TestSupport;
using GrpCurl.Net.Studio.ViewModels.Connections;

namespace GrpCurl.Net.Studio.Tests.Unit;

/// <summary>FR-037: parsing certificate facts (subject/issuer/validity/SHA-256) from a file.</summary>
public sealed class CertificateInspectorTests : IDisposable
{
    private readonly List<string> _temp = [];

    public void Dispose()
    {
        foreach (var f in _temp)
        {
            try { File.Delete(f); } catch (IOException) { /* best-effort */ }
        }
    }

    private string Pem(string subject = "CN=grpcn-test", int notBeforeDays = -1, int notAfterDays = 365)
    {
        var path = TestCertificate.WritePem(subject, notBeforeDays, notAfterDays);
        _temp.Add(path);
        return path;
    }

    [Fact]
    public void Reads_subject_issuer_validity_and_fingerprint_from_a_pem_certificate()
    {
        var path = Pem("CN=example.test");

        var facts = CertificateInspector.TryRead(path).ShouldNotBeNull();

        facts.Subject.ShouldContain("example.test");
        facts.Issuer.ShouldContain("example.test"); // self-signed: issuer == subject
        facts.IsOutOfValidity.ShouldBeFalse();
        facts.Sha256Fingerprint.ShouldBe(TestCertificate.Sha256Of(path));
        facts.Sha256Fingerprint.ShouldContain(":"); // colon-grouped hex
    }

    [Fact]
    public void Flags_an_expired_certificate_as_out_of_validity()
    {
        var facts = CertificateInspector.TryRead(Pem("CN=old", notBeforeDays: -30, notAfterDays: -1)).ShouldNotBeNull();

        facts.IsExpired.ShouldBeTrue();
        facts.IsOutOfValidity.ShouldBeTrue();
        facts.ValidityText.ShouldContain("expired");
    }

    [Fact]
    public void Returns_null_for_missing_blank_or_unparseable_input()
    {
        CertificateInspector.TryRead(null).ShouldBeNull();
        CertificateInspector.TryRead("   ").ShouldBeNull();
        CertificateInspector.TryRead("/no/such/file.pem").ShouldBeNull();

        var garbage = Path.Combine(Path.GetTempPath(), $"grpcn-garbage-{Guid.NewGuid():N}.pem");
        File.WriteAllText(garbage, "not a certificate");
        _temp.Add(garbage);
        CertificateInspector.TryRead(garbage).ShouldBeNull();
    }
}
