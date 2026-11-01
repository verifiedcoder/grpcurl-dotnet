using GrpCurl.Net.Studio.Services;
using GrpCurl.Net.Studio.Services.Secrets;
using GrpCurl.Net.Studio.TestSupport;
using GrpCurl.Net.Studio.ViewModels;
using GrpCurl.Net.Studio.ViewModels.Models.Connections;
using GrpCurl.Net.Studio.ViewModels.Services;

namespace GrpCurl.Net.Studio.Tests.Unit;

public sealed class WorkspaceImportTlsTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "grpcn-tls-consent-" + Guid.NewGuid().ToString("N"));
    private readonly List<JsonWorkspaceStore> _stores = [];
    private string Source => Path.Combine(_root, "source", "incoming.json");
    private string Destination => Path.Combine(_root, "destination", "workspace.json");
    private string TrustPath => Path.Combine(_root, "private", "workspace-trust.json");

    private static WorkspaceModel Incoming() => new()
    {
        TlsProfiles =
        [
            new TlsProfile { Id = "first", Name = "Duplicate", InsecureSkipVerify = true },
            new TlsProfile { Id = "second", Name = "Duplicate", InsecureSkipVerify = true, CaCertPath = "ca.pem", RevocationMode = "offline" },
            new TlsProfile { Id = "ca-only", Name = "Custom root", CaCertPath = "ca.pem" },
            new TlsProfile { Id = "safe", Name = "System roots" }
        ],
        Connections = [new SavedConnection { Id = "connection", Name = "API", TlsProfileId = "second" }],
        Environments = [new WorkspaceEnvironment { Id = "environment", Name = "Test" }]
    };

    private JsonWorkspaceStore Store(WorkspaceTrustStore? trust = null)
    {
        var store = new JsonWorkspaceStore(Destination, trust: trust ?? new WorkspaceTrustStore(TrustPath));
        _stores.Add(store);
        return store;
    }

    private async Task WriteSource(WorkspaceModel? model = null)
    {
        _ = Directory.CreateDirectory(Path.GetDirectoryName(Source)!);
        await File.WriteAllBytesAsync(Source, WorkspaceSerializer.SerializeToUtf8(model ?? Incoming()));
    }

    public void Dispose()
    {
        foreach (var store in _stores) { store.ReleaseLock(); }
        if (Directory.Exists(_root)) { Directory.Delete(_root, true); }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void Merger_applies_only_selected_source_IDs_without_mutating_inputs(int count)
    {
        var incoming = Incoming();
        var current = new WorkspaceModel
        {
            TlsProfiles = [new TlsProfile { Id = "first", Name = "Duplicate", InsecureSkipVerify = true }]
        };
        var originalSource = WorkspaceSerializer.Serialize(incoming);
        var originalDestination = WorkspaceSerializer.Serialize(current);
        var selected = new[] { "second", "first" }.Take(count).ToArray();
        var (merged, summary) = WorkspaceMerger.Merge(current, incoming, selected);
        WorkspaceSerializer.Serialize(incoming).ShouldBe(originalSource);
        WorkspaceSerializer.Serialize(current).ShouldBe(originalDestination);
        merged.TlsProfiles[0].InsecureSkipVerify.ShouldBeTrue();
        merged.TlsProfiles.Skip(1).Count(p => p.InsecureSkipVerify).ShouldBe(count);
        merged.TlsProfiles.Skip(1).Select(p => p.Id).Intersect(incoming.TlsProfiles.Select(p => p.Id)).ShouldBeEmpty();
        merged.Connections.Single().TlsProfileId.ShouldBe(merged.TlsProfiles[2].Id);
        merged.TlsProfiles[2].CaCertPath.ShouldBe("ca.pem");
        merged.TlsProfiles[2].RevocationMode.ShouldBe("offline");
        summary.TlsSecurity.Select(e => e.SourceProfileId).ShouldBe(["first", "second", "ca-only"]);
        summary.TlsSecurity.Select(e => e.SelectedBypass).ShouldBe([count == 2, count >= 1, false]);
        summary.TlsSecurity[0].OriginalName.ShouldBe("Duplicate");
        summary.TlsSecurity[0].DestinationName.ShouldBe("Duplicate (imported)");
        summary.TlsSecurity[1].DestinationName.ShouldBe("Duplicate (imported 2)");
        merged.TlsProfiles[1].CaCertPath = "changed";
        merged.Connections[0].DescriptorSource.ProtosetPaths.Add("changed");
        merged.Environments[0].Name = "changed";
        WorkspaceSerializer.Serialize(incoming).ShouldBe(originalSource);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(" first")]
    [InlineData("first\n")]
    [InlineData("first")]
    public void Ambiguous_profile_identities_are_rejected_before_merging(string? id)
    {
        var incoming = Incoming();
        incoming.TlsProfiles[1].Id = id!;
        _ = Should.Throw<WorkspaceTrustException>(() => WorkspaceMerger.Merge(new WorkspaceModel(), incoming));
    }

    [Theory]
    [InlineData("unknown")]
    [InlineData("safe")]
    [InlineData("ca-only")]
    [InlineData(null)]
    public void Merger_rejects_unknown_or_ineligible_selections(string? id)
        => _ = Should.Throw<WorkspaceTrustException>(() => WorkspaceMerger.Merge(new WorkspaceModel(), Incoming(), [id!]));

    [Fact]
    public void Merger_rejects_duplicate_selections()
        => _ = Should.Throw<WorkspaceTrustException>(() => WorkspaceMerger.Merge(new WorkspaceModel(), Incoming(), ["first", "first"]));

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task Dialog_preview_commit_and_restart_agree_and_reimport_starts_unchecked(int count)
    {
        await WriteSource();
        var sourceBytes = await File.ReadAllBytesAsync(Source);
        var store = Store();
        await store.SaveAsync(new WorkspaceModel { TlsProfiles = [new TlsProfile { Id = "existing", Name = "Duplicate", InsecureSkipVerify = true }] });
        var preparation = await store.PrepareAsync(Source);
        var dialog = new WorkspaceImportDialogViewModel(preparation, store.Current);
        dialog.TlsProfiles.ShouldAllBe(i => !i.DisableCertificateVerification);
        foreach (var row in dialog.TlsProfiles.Where(i => i.Entry.RequestedBypass).Take(count)) { row.DisableCertificateVerification = true; }
        var decision = dialog.CreateDecision();
        await store.CommitImportAsync(preparation, decision);
        var profiles = store.Current.TlsProfiles.Skip(1).ToArray();
        profiles.Count(p => p.InsecureSkipVerify).ShouldBe(count);
        foreach (var entry in dialog.Summary.TlsSecurity)
        {
            var profile = profiles.Single(p => p.Name == entry.DestinationName);
            profile.InsecureSkipVerify.ShouldBe(entry.SelectedBypass);
            profile.CaCertPath.ShouldBe(entry.CustomCaPath);
        }
        store.Current.TlsProfiles[0].InsecureSkipVerify.ShouldBeTrue();
        store.ReleaseLock();
        var restarted = Store();
        (await restarted.LoadAsync()).TlsProfiles.Select(p => p.InsecureSkipVerify)
            .ShouldBe(store.Current.TlsProfiles.Select(p => p.InsecureSkipVerify));
        (await restarted.PrepareAsync(Destination)).RequiresApproval.ShouldBeFalse();
        new WorkspaceImportDialogViewModel(await restarted.PrepareAsync(Source), restarted.Current)
            .TlsProfiles.ShouldAllBe(i => !i.DisableCertificateVerification);
        (await File.ReadAllBytesAsync(Source)).ShouldBe(sourceBytes);
        var saved = await File.ReadAllTextAsync(Destination);
        saved.ShouldNotContain("approvedBypassProfileIds");
        (await File.ReadAllTextAsync(TrustPath)).ShouldNotContain("insecureSkipVerify");
    }

    [Fact]
    public async Task Decision_is_immutable_and_cannot_be_reused_for_another_preparation()
    {
        await WriteSource();
        var store = Store();
        var preparation = await store.PrepareAsync(Source);
        var ids = new[] { "first" };
        var decision = new WorkspaceImportDecision(preparation, ids);
        ids[0] = "second";
        decision.ApprovedBypassProfileIds.ShouldBe(["first"]);
        var other = await store.PrepareAsync(Source);
        _ = await Should.ThrowAsync<WorkspaceTrustException>(() => store.CommitImportAsync(other, decision));
        store.Current.TlsProfiles.ShouldBeEmpty();
        File.Exists(Destination).ShouldBeFalse();
        var preview = preparation.Workspace;
        preview.TlsProfiles[0].Id = "safe";
        preview.TlsProfiles[0].InsecureSkipVerify = false;
        await store.CommitImportAsync(preparation, decision);
        store.Current.TlsProfiles[0].InsecureSkipVerify.ShouldBeTrue();
        store.Current.TlsProfiles[3].InsecureSkipVerify.ShouldBeFalse();
    }

    [Theory]
    [InlineData("unknown")]
    [InlineData("safe")]
    [InlineData("first,first")]
    public async Task Invalid_decisions_are_rejected_at_creation(string ids)
    {
        await WriteSource();
        var preparation = await Store().PrepareAsync(Source);
        _ = Should.Throw<WorkspaceTrustException>(() => new WorkspaceImportDecision(preparation, ids.Split(',')));
    }

    [Theory]
    [InlineData("{\"name\":\"Missing ID\",\"insecureSkipVerify\":true}")]
    [InlineData("{\"id\":null}")]
    [InlineData("{\"id\":\"\"}")]
    [InlineData("{\"id\":\"a\",\"id\":\"b\"}")]
    [InlineData("{\"id\":\"a\"},{\"id\":\"a\"}")]
    public async Task Malformed_document_identities_never_reach_import_consent(string profiles)
    {
        await WriteSource();
        await File.WriteAllTextAsync(Source, "{\"tlsProfiles\":[" + profiles + "]}");
        var store = Store();
        var preparation = await store.PrepareAsync(Source);
        _ = Should.Throw<WorkspaceTrustException>(() => new WorkspaceImportDialogViewModel(preparation, store.Current));
        _ = await Should.ThrowAsync<WorkspaceTrustException>(() => store.CommitImportAsync(preparation));
        store.Current.TlsProfiles.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("bytes")]
    [InlineData("revoke")]
    [InlineData("link")]
    public async Task Changed_source_or_path_approval_cannot_commit_selected_bypass(string change)
    {
        await WriteSource();
        var store = Store();
        var ca = Path.Combine(Path.GetDirectoryName(Source)!, "ca.pem");
        if (change == "link")
        {
            await File.WriteAllTextAsync(ca + ".one", "not a certificate");
            await File.WriteAllTextAsync(ca + ".two", "also not a certificate");
            _ = File.CreateSymbolicLink(ca, ca + ".one");
        }
        await store.SaveAsync(WorkspaceModel.Empty());
        var before = store.Current;
        var bytes = await File.ReadAllBytesAsync(Destination);
        var preparation = await store.PrepareAsync(Source);
        var decision = new WorkspaceImportDecision(preparation, ["first"]);
        switch (change)
        {
            case "bytes": await File.AppendAllTextAsync(Source, " "); break;
            case "revoke": new WorkspaceTrustStore(TrustPath).Revoke(null); break;
            case "link": File.Delete(ca); _ = File.CreateSymbolicLink(ca, ca + ".two"); break;
        }
        _ = await Should.ThrowAsync<WorkspaceTrustException>(() => store.CommitImportAsync(preparation, decision));
        store.Current.ShouldBeSameAs(before);
        (await File.ReadAllBytesAsync(Destination)).ShouldBe(bytes);
    }

    [Fact]
    public async Task Unsaved_destination_keeps_the_safe_default_and_pending_changes()
    {
        await WriteSource();
        var store = Store();
        _ = store.NewWorkspace();
        await store.CommitImportAsync(await store.PrepareAsync(Source));
        store.Current.TlsProfiles.ShouldAllBe(p => !p.InsecureSkipVerify);
        store.IsDirty.ShouldBeTrue();
        store.CurrentPath.ShouldBeNull();
        File.Exists(Destination).ShouldBeFalse();
        store.ApprovalDialogs = new FakeDialogService
        {
            OnShowDialog = d => ((WorkspaceApprovalDialogViewModel)d).Items.Select(i => i.Reference.ApprovalKey).ToArray()
        };
        await store.SaveAsAsync(store.Current, Destination);
        WorkspaceSerializer.Deserialize(await File.ReadAllTextAsync(Destination)).TlsProfiles.ShouldAllBe(p => !p.InsecureSkipVerify);
    }

    [Fact]
    public async Task Workspace_write_failure_preserves_active_and_persisted_destination()
    {
        await WriteSource();
        var store = Store();
        await store.SaveAsync(WorkspaceModel.Empty());
        var before = store.Current;
        var bytes = await File.ReadAllBytesAsync(Destination);
        _ = Directory.CreateDirectory(Destination + ".tmp");
        var preparation = await store.PrepareAsync(Source);
        _ = await Should.ThrowAsync<UnauthorizedAccessException>(() => store.CommitImportAsync(preparation, new WorkspaceImportDecision(preparation, ["first"])));
        store.Current.ShouldBeSameAs(before);
        (await File.ReadAllBytesAsync(Destination)).ShouldBe(bytes);
    }

    [Fact]
    public async Task Approval_write_failure_reports_committed_workspace_and_retains_selected_TLS_outcome()
    {
        await WriteSource();
        var files = new SecureSecretFile((stage, _) => { if (stage == SecretFileStage.BeforeReplace) { throw new IOException("injected approval failure"); } });
        var store = Store(new WorkspaceTrustStore(TrustPath, files));
        await store.SaveAsync(WorkspaceModel.Empty());
        var preparation = await store.PrepareAsync(Source);
        var error = await Should.ThrowAsync<WorkspaceTrustException>(() => store.CommitImportAsync(preparation, new WorkspaceImportDecision(preparation, ["second"])));
        error.WorkspaceSaved.ShouldBeTrue();
        store.Current.TlsProfiles.Select(p => p.InsecureSkipVerify).ShouldBe([false, true, false, false]);
        WorkspaceSerializer.Deserialize(await File.ReadAllTextAsync(Destination)).TlsProfiles[1].InsecureSkipVerify.ShouldBeTrue();
        store.IsDirty.ShouldBeFalse();
        (await Store().PrepareAsync(Destination)).RequiresApproval.ShouldBeTrue();
    }
}
