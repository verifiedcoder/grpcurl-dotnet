using GrpCurl.Net.Studio.Services;
using GrpCurl.Net.Studio.ViewModels.Models.Connections;
using GrpCurl.Net.Studio.ViewModels.Services;

namespace GrpCurl.Net.Studio.Tests.Unit;

public sealed class WorkspaceImportTlsBaselineTests
{
    [Fact]
    public async Task Import_without_explicit_consent_persists_certificate_verification_enabled()
    {
        var root = Path.Combine(Path.GetTempPath(), "grpcn-import-tls-" + Guid.NewGuid().ToString("N"));
        _ = Directory.CreateDirectory(root);
        var source = Path.Combine(root, "incoming.json");
        var destination = Path.Combine(root, "active.json");
        var store = new JsonWorkspaceStore(destination, trust: new WorkspaceTrustStore(Path.Combine(root, "trust.json")));
        try
        {
            await File.WriteAllBytesAsync(source, WorkspaceSerializer.SerializeToUtf8(new WorkspaceModel
            {
                TlsProfiles = [new TlsProfile { Id = "insecure", Name = "Shared TLS", InsecureSkipVerify = true }]
            }));
            await store.SaveAsync(WorkspaceModel.Empty());
            await store.CommitImportAsync(await store.PrepareAsync(source));
            var saved = WorkspaceSerializer.Deserialize(await File.ReadAllTextAsync(destination));
            saved.TlsProfiles.Single().InsecureSkipVerify.ShouldBeFalse();
        }
        finally
        {
            store.ReleaseLock();
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Import_summary_discloses_requested_certificate_verification_bypass()
    {
        var (_, summary) = WorkspaceMerger.Merge(new WorkspaceModel(), new WorkspaceModel
        {
            TlsProfiles = [new TlsProfile { Name = "Shared TLS", InsecureSkipVerify = true }]
        });
        summary.Describe().ShouldContain("certificate verification");
    }

    [Fact]
    public void Import_summary_discloses_custom_CA_path()
    {
        var (_, summary) = WorkspaceMerger.Merge(new WorkspaceModel(), new WorkspaceModel
        {
            TlsProfiles = [new TlsProfile { Name = "Shared TLS", CaCertPath = "certs/team-ca.pem" }]
        });
        summary.Describe().ShouldContain("certs/team-ca.pem");
    }
}
