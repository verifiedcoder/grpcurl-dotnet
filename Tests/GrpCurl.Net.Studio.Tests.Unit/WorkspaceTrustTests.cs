using GrpCurl.Net.Studio.Services;
using GrpCurl.Net.Studio.Services.Secrets;
using GrpCurl.Net.Studio.TestSupport;
using GrpCurl.Net.Studio.ViewModels.Models.Connections;
using GrpCurl.Net.Studio.ViewModels.Services;
using GrpCurl.Net.DescriptorSources;
using GrpCurl.Net.Utilities;
using Grpc.Core;

namespace GrpCurl.Net.Studio.Tests.Unit;

public sealed class WorkspaceTrustTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "grpcn-trust-" + Guid.NewGuid().ToString("N"));
    private readonly List<JsonWorkspaceStore> _stores = [];
    private string WorkspacePath => Path.Combine(_root, "project", "workspace.json");
    private string TrustPath => Path.Combine(_root, "private", "workspace-trust.json");
    private JsonWorkspaceStore Store(WorkspaceTrustStore? trust = null)
    {
        var store = new JsonWorkspaceStore(WorkspacePath, trust: trust ?? new WorkspaceTrustStore(TrustPath));
        _stores.Add(store);
        return store;
    }
    public void Dispose()
    {
        foreach (var store in _stores) { store.ReleaseLock(); }
        if (Directory.Exists(_root)) { Directory.Delete(_root, true); }
    }
    private async Task<WorkspaceModel> Write(string reference = "../external/missing.protoset", string? path = null)
    {
        path ??= WorkspacePath;
        _ = Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var model = WorkspaceModel.Empty();
        model.Connections.Add(new SavedConnection
        {
            Id = "connection", Name = "Example", Address = "localhost:1", Transport = TransportMode.Plaintext,
            DescriptorSource = new DescriptorSourceConfig { Mode = DescriptorMode.Protoset, ProtosetPaths = [reference] }
        });
        await File.WriteAllBytesAsync(path, WorkspaceSerializer.SerializeToUtf8(model));
        return model;
    }
    private static async Task<WorkspacePreparation> Approve(JsonWorkspaceStore store, string path)
    {
        var prepared = await store.PrepareAsync(path);
        for (var i = 0; prepared.RequiresApproval && i < 10; i++)
        {
            prepared = await store.ApproveAsync(prepared, prepared.References.Where(r => r.ApprovalReason is not null).Select(r => r.ApprovalKey).ToArray());
        }
        prepared.RequiresApproval.ShouldBeFalse();
        return prepared;
    }

    [Theory]
    [InlineData("descriptors/api.protoset", false)]
    [InlineData("../project-sibling/api.protoset", true)]
    [InlineData("../external/api.protoset", true)]
    [InlineData("child/../../external/api.protoset", true)]
    [InlineData("..hidden/api.protoset", false)]
    public async Task Component_containment_distinguishes_traversal_and_sibling_prefixes(string path, bool approval)
    {
        _ = await Write(path);
        var store = Store();
        var prepared = await store.PrepareAsync(WorkspacePath);
        prepared.RequiresApproval.ShouldBe(approval);
        var reference = prepared.References.ShouldHaveSingleItem();
        reference.OwnerId.ShouldBe("connection");
        reference.Owner.ShouldBe("Example");
        reference.Field.ShouldBe("Descriptor protoset");
        reference.OriginalPath.ShouldBe(path);
        reference.AbsolutePath.ShouldBe(Path.GetFullPath(path, Path.GetDirectoryName(WorkspacePath)!));
        store.Current.Connections.ShouldBeEmpty();
        store.RecentWorkspaces.ShouldBeEmpty();
        File.Exists(WorkspacePath + ".lock").ShouldBeFalse();
        File.Exists(TrustPath).ShouldBeFalse();
    }

    [Theory]
    [InlineData("bad\0path")]
    [InlineData("bad\npath")]
    [InlineData("http://host/path")]
    [InlineData(" path")]
    [InlineData("wildcard*.proto")]
    public async Task Malformed_references_fail_before_activation(string path)
    {
        _ = await Write(path);
        _ = await Should.ThrowAsync<WorkspaceTrustException>(() => Store().PrepareAsync(WorkspacePath));
    }

    [Fact]
    public async Task Platform_specific_roots_and_separators_are_explicit()
    {
        var paths = OperatingSystem.IsWindows() ? new[] { @"C:relative", @"\rooted", @"C:\bad:stream", @"\\?\C:\file" }
            : new[] { @"C:\absolute\file", @"..\escape", @"\\server\share\file" };
        foreach (var path in paths)
        {
            _ = await Write(path);
            _ = await Should.ThrowAsync<WorkspaceTrustException>(() => Store().PrepareAsync(WorkspacePath));
        }
    }

    [Fact]
    public async Task All_tls_fields_and_descriptor_fields_are_reported()
    {
        var model = await Write();
        model.Connections[0].DescriptorSource.ProtoFiles.Add("../outside/api.proto");
        model.Connections[0].DescriptorSource.ImportPaths.Add("../outside/imports");
        model.TlsProfiles.Add(new TlsProfile { Id = "tls", Name = "Client TLS", CaCertPath = "../outside/ca", ClientCertPath = "../outside/cert", ClientKeyPath = "../outside/key" });
        await File.WriteAllBytesAsync(WorkspacePath, WorkspaceSerializer.SerializeToUtf8(model));
        var prepared = await Store().PrepareAsync(WorkspacePath);
        prepared.References.Count.ShouldBe(7); // six configured fields and protoc's implicit lookup directory
        prepared.References.ShouldAllBe(r => r.ApprovalReason != null);
        prepared.References.Count(r => r.OwnerId == "tls").ShouldBe(3);
    }

    [Fact]
    public async Task A_declined_or_incomplete_approval_preserves_active_state_and_input()
    {
        _ = await Write();
        var store = Store();
        var original = store.NewWorkspace();
        original.Name = "My unsaved edits";
        await store.SaveAsync(original);
        var bytes = await File.ReadAllBytesAsync(WorkspacePath);
        var result = await WorkspaceApproval.PrepareAsync(store, new FakeDialogService(), WorkspacePath);
        result.ShouldBeNull();
        store.Current.ShouldBeSameAs(original);
        store.IsDirty.ShouldBeTrue();
        store.CurrentPath.ShouldBeNull();
        (await File.ReadAllBytesAsync(WorkspacePath)).ShouldBe(bytes);
        var preparation = await store.PrepareAsync(WorkspacePath);
        _ = await Should.ThrowAsync<WorkspaceTrustException>(() => store.ApproveAsync(preparation, []));
        _ = await Should.ThrowAsync<WorkspaceTrustException>(() => store.CommitOpenAsync(preparation));
    }

    [Fact]
    public async Task Changed_document_and_foreign_preparation_cannot_commit()
    {
        _ = await Write();
        var store = Store();
        var preparation = await Approve(store, WorkspacePath);
        _ = await Should.ThrowAsync<WorkspaceTrustException>(() => Store().CommitOpenAsync(preparation));
        await File.AppendAllTextAsync(WorkspacePath, "\n");
        _ = await Should.ThrowAsync<WorkspaceTrustException>(() => store.CommitOpenAsync(preparation));
        store.Current.Connections.ShouldBeEmpty();
        File.Exists(TrustPath).ShouldBeFalse();
    }

    [Fact]
    public async Task Preview_model_mutations_cannot_smuggle_new_references()
    {
        _ = await Write("inside.protoset");
        var store = Store();
        var preparation = await store.PrepareAsync(WorkspacePath);
        preparation.Workspace.Connections[0].DescriptorSource.ProtosetPaths.Add("../outside/smuggled");
        _ = await store.CommitOpenAsync(preparation);
        store.Current.Connections[0].DescriptorSource.ProtosetPaths.Count.ShouldBe(1);
    }

    [Fact]
    public async Task Approvals_survive_restart_but_external_edits_and_copies_require_new_consent()
    {
        _ = await Write();
        var store = Store();
        _ = await store.CommitOpenAsync(await Approve(store, WorkspacePath));
        store.ReleaseLock();
        (await Store().PrepareAsync(WorkspacePath)).RequiresApproval.ShouldBeFalse();
        var copy = Path.Combine(_root, "copy", "workspace.json");
        _ = Directory.CreateDirectory(Path.GetDirectoryName(copy)!);
        File.Copy(WorkspacePath, copy);
        (await Store().PrepareAsync(copy)).RequiresApproval.ShouldBeTrue();
        await File.AppendAllTextAsync(WorkspacePath, " ");
        (await Store().PrepareAsync(WorkspacePath)).RequiresApproval.ShouldBeTrue();
    }

    [Fact]
    public async Task Authorized_save_advances_fingerprint_and_new_references_require_consent()
    {
        _ = await Write();
        var store = Store();
        _ = await store.CommitOpenAsync(await Approve(store, WorkspacePath));
        var changed = store.Current.Copy();
        changed.Name = "Renamed locally";
        await store.SaveAsync(changed);
        (await Store().PrepareAsync(WorkspacePath)).RequiresApproval.ShouldBeFalse();
        var old = await File.ReadAllBytesAsync(WorkspacePath);
        changed.Connections[0].DescriptorSource.ProtosetPaths.Add(Path.Combine(_root, "new-external"));
        _ = await Should.ThrowAsync<WorkspaceTrustException>(() => store.SaveAsync(changed));
        (await File.ReadAllBytesAsync(WorkspacePath)).ShouldBe(old);
    }

    [Fact]
    public async Task Startup_defers_approval_and_does_not_lock_quarantine_or_overwrite()
    {
        _ = await Write();
        var bytes = await File.ReadAllBytesAsync(WorkspacePath);
        var store = Store();
        (await store.LoadAsync()).Connections.ShouldBeEmpty();
        store.CurrentPath.ShouldBeNull();
        store.PendingStartupPath.ShouldBe(WorkspacePath);
        File.Exists(WorkspacePath + ".lock").ShouldBeFalse();
        File.Exists(WorkspacePath + ".corrupt").ShouldBeFalse();
        await store.SaveNowAsync();
        (await File.ReadAllBytesAsync(WorkspacePath)).ShouldBe(bytes);
    }

    [Fact]
    public async Task Malformed_trust_grants_nothing_and_can_be_reset_explicitly()
    {
        _ = await Write();
        _ = Directory.CreateDirectory(Path.GetDirectoryName(TrustPath)!);
        await File.WriteAllTextAsync(TrustPath, "{broken");
        var store = Store();
        (await store.PrepareAsync(WorkspacePath)).RequiresApproval.ShouldBeTrue();
        store.TrustMessage.ShouldNotBeNull().ShouldContain("No remembered approvals");
        var prepared = await Approve(store, WorkspacePath);
        _ = await Should.ThrowAsync<WorkspaceTrustException>(() => store.CommitOpenAsync(prepared));
        store.Current.Connections.ShouldBeEmpty();
        store.Trust.Revoke(null);
        _ = await store.CommitOpenAsync(await Approve(store, WorkspacePath));
        store.Trust.ApprovedWorkspaces.ShouldContain(WorkspaceReferenceAssessment.ResolveTarget(WorkspacePath, _ => true).Target);
        if (!OperatingSystem.IsWindows()) { File.GetUnixFileMode(TrustPath).ShouldBe(UnixFileMode.UserRead | UnixFileMode.UserWrite); }
    }

    [Fact]
    public async Task Approval_write_failure_preserves_workspace_before_open_commit()
    {
        _ = await Write();
        var files = new SecureSecretFile((stage, _) => { if (stage == SecretFileStage.BeforeReplace) { throw new IOException("injected trust write failure"); } });
        var store = Store(new WorkspaceTrustStore(TrustPath, files));
        var bytes = await File.ReadAllBytesAsync(WorkspacePath);
        _ = await Should.ThrowAsync<WorkspaceTrustException>(async () => await store.CommitOpenAsync(await Approve(store, WorkspacePath)));
        store.Current.Connections.ShouldBeEmpty();
        store.RecentWorkspaces.ShouldBeEmpty();
        (await File.ReadAllBytesAsync(WorkspacePath)).ShouldBe(bytes);
    }

    [Fact]
    public async Task Save_reports_partial_success_when_trust_persistence_fails()
    {
        _ = await Write();
        var fail = false;
        var files = new SecureSecretFile((stage, _) => { if (fail && stage == SecretFileStage.BeforeReplace) { throw new IOException("injected"); } });
        var store = Store(new WorkspaceTrustStore(TrustPath, files));
        _ = await store.CommitOpenAsync(await Approve(store, WorkspacePath));
        fail = true;
        var model = store.Current.Copy();
        model.Name = "Saved despite trust failure";
        var error = await Should.ThrowAsync<WorkspaceTrustException>(() => store.SaveAsync(model));
        error.Message.ShouldContain("workspace was saved");
        error.WorkspaceSaved.ShouldBeTrue();
        (await File.ReadAllTextAsync(WorkspacePath)).ShouldContain(model.Name);
        (await Store().PrepareAsync(WorkspacePath)).RequiresApproval.ShouldBeTrue();
        _ = Should.Throw<WorkspaceTrustException>(() => store.AuthorizeConnection(model.Connections[0]));
    }

    [Fact]
    public async Task Cross_instance_revocation_cancels_live_access_and_blocks_new_reads()
    {
        _ = await Write();
        var store = Store();
        _ = await store.CommitOpenAsync(await Approve(store, WorkspacePath));
        var access = new WorkspaceFileAccess(store);
        using var lease = access.Begin(store.Current.Connections[0], TestContext.Current.CancellationToken);
        new WorkspaceTrustStore(TrustPath).Revoke(WorkspacePath);
        _ = await Should.ThrowAsync<OperationCanceledException>(() => Task.Delay(TimeSpan.FromSeconds(3), lease.Token));
        _ = Should.Throw<WorkspaceTrustException>(() => access.Begin(store.Current.Connections[0], CancellationToken.None));
        store.Current.Connections.Count.ShouldBe(1);
    }

    [Fact]
    public async Task Import_transfers_approved_locations_after_ID_remapping_and_portable_save()
    {
        _ = await Write("inside.protoset");
        var imported = Path.Combine(_root, "imported", "source.json");
        _ = await Write("../outside/external.protoset", imported);
        var store = Store();
        _ = await store.OpenAsync(WorkspacePath);
        await store.CommitImportAsync(await Approve(store, imported));
        store.Current.Connections.Count.ShouldBe(2);
        store.Current.Connections.Select(c => c.Id).Distinct().Count().ShouldBe(2);
        (await Store().PrepareAsync(WorkspacePath)).RequiresApproval.ShouldBeFalse();
        (await File.ReadAllTextAsync(WorkspacePath)).ShouldNotContain("Approval");
    }

    [Fact]
    public async Task Consumption_guard_rejects_unconfigured_files_before_reading()
    {
        _ = await Write("inside.protoset");
        var store = Store();
        _ = await store.OpenAsync(WorkspacePath);
        var guard = new WorkspaceFileAccess(store).Guard(store.Current.Connections[0]);
        guard(Path.Combine(_root, "project", "inside.protoset"), false);
        _ = Should.Throw<WorkspaceTrustException>(() => guard(Path.Combine(_root, "project", "not-configured"), false));
    }

    [Fact]
    public async Task External_links_are_not_inspected_before_consent_and_unresolvable_links_fail_closed()
    {
        _ = await Write("../loop");
        _ = File.CreateSymbolicLink(Path.Combine(_root, "loop"), "loop");
        var store = Store();
        var prepared = await store.PrepareAsync(WorkspacePath); // must not follow the external cycle yet
        prepared.RequiresApproval.ShouldBeTrue();
        var error = await Should.ThrowAsync<WorkspaceTrustException>(() => store.ApproveAsync(prepared, [prepared.References[0].ApprovalKey]));
        error.Message.ShouldContain("cycle");
        store.Current.Connections.ShouldBeEmpty();
    }

    [Fact]
    public async Task Contained_link_escape_requires_approval_and_retargeting_invalidates_it()
    {
        _ = await Write("linked.protoset");
        var first = Path.Combine(_root, "first.protoset");
        var second = Path.Combine(_root, "second.protoset");
        await File.WriteAllTextAsync(first, "not read at approval time");
        await File.WriteAllTextAsync(second, "also not read");
        var link = Path.Combine(_root, "project", "linked.protoset");
        _ = File.CreateSymbolicLink(link, first);
        var store = Store();
        var prepared = await store.PrepareAsync(WorkspacePath);
        prepared.References[0].ResolvedTarget.ShouldBe(first);
        prepared.RequiresApproval.ShouldBeTrue();
        _ = await store.CommitOpenAsync(await Approve(store, WorkspacePath));
        File.Delete(link);
        _ = File.CreateSymbolicLink(link, second);
        (await Store().PrepareAsync(WorkspacePath)).RequiresApproval.ShouldBeTrue();
        _ = Should.Throw<WorkspaceTrustException>(() => store.AuthorizeConnection(store.Current.Connections[0]));
    }

    [Fact]
    public async Task Linked_directory_escapes_require_individual_approval_and_guarded_lookup_rechecks_them()
    {
        var model = await Write("inside.protoset");
        var imports = Path.Combine(_root, "project", "protos");
        var external = Path.Combine(_root, "external");
        _ = Directory.CreateDirectory(imports);
        _ = Directory.CreateDirectory(external);
        await File.WriteAllTextAsync(Path.Combine(external, "api.proto"), "syntax = \"proto3\";");
        _ = Directory.CreateSymbolicLink(Path.Combine(imports, "linked"), external);
        model.Connections[0].DescriptorSource.ImportPaths.Add(imports);
        await File.WriteAllBytesAsync(WorkspacePath, WorkspaceSerializer.SerializeToUtf8(model));
        var store = Store();
        var prepared = await store.PrepareAsync(WorkspacePath);
        prepared.References.ShouldContain(r => r.Field == "Linked descriptor lookup entry" && r.ApprovalReason != null);
        _ = await store.CommitOpenAsync(await Approve(store, WorkspacePath));
        new WorkspaceFileAccess(store).Guard(store.Current.Connections[0])(Path.Combine(imports, "linked", "api.proto"), false);
    }

    [Fact]
    public async Task Links_remaining_inside_the_workspace_are_automatic_and_dangling_links_fail_closed()
    {
        _ = await Write("linked.protoset");
        var target = Path.Combine(_root, "project", "target.protoset");
        await File.WriteAllTextAsync(target, "content not opened");
        var link = Path.Combine(_root, "project", "linked.protoset");
        _ = File.CreateSymbolicLink(link, target);
        (await Store().PrepareAsync(WorkspacePath)).RequiresApproval.ShouldBeFalse();
        File.Delete(target);
        _ = await Should.ThrowAsync<WorkspaceTrustException>(() => Store().PrepareAsync(WorkspacePath));
    }

    [Fact]
    public async Task Revoking_one_workspace_preserves_other_active_authorizations()
    {
        _ = await Write();
        var store = Store();
        _ = await store.CommitOpenAsync(await Approve(store, WorkspacePath));
        var secondPath = Path.Combine(_root, "second", "workspace.json");
        _ = await Write("../external/second", secondPath);
        var second = Store();
        _ = await second.CommitOpenAsync(await Approve(second, secondPath));
        store.Trust.Revoke(WorkspacePath);
        _ = Should.Throw<WorkspaceTrustException>(() => store.AuthorizeConnection(store.Current.Connections[0]));
        _ = second.AuthorizeConnection(second.Current.Connections[0]);
        second.Trust.Revoke(null);
        _ = Should.Throw<WorkspaceTrustException>(() => second.AuthorizeConnection(second.Current.Connections[0]));
    }

    [Fact]
    public async Task A_previous_intermediate_link_target_is_not_a_remembered_final_target()
    {
        _ = await Write("linked.protoset");
        var middle = Path.Combine(_root, "middle");
        var final = Path.Combine(_root, "final");
        var link = Path.Combine(_root, "project", "linked.protoset");
        await File.WriteAllTextAsync(final, "not parsed");
        _ = File.CreateSymbolicLink(middle, final);
        _ = File.CreateSymbolicLink(link, middle);
        var store = Store();
        _ = await store.CommitOpenAsync(await Approve(store, WorkspacePath));
        File.Delete(middle);
        await File.WriteAllTextAsync(middle, "different final location");
        (await Store().PrepareAsync(WorkspacePath)).RequiresApproval.ShouldBeTrue();
        _ = Should.Throw<WorkspaceTrustException>(() => store.AuthorizeConnection(store.Current.Connections[0]));
    }

    [Fact]
    public async Task Native_descriptor_and_tls_boundaries_invoke_host_policy_before_reading()
    {
        var denied = new WorkspaceTrustException("Denied by host before reading");
        var missing = Path.Combine(_root, "not-present");
        void Deny(string path, bool directory) => throw denied;
        var error = await Should.ThrowAsync<WorkspaceTrustException>(() => DescriptorSourceFactory.CreateAsync(
            null, [missing], [], [], new GrpcChannelFactory.ChannelOptions(), new Metadata(), TestContext.Current.CancellationToken,
            descriptorOptions: new DescriptorSourceOptions { FileAccessGuard = Deny }));
        error.ShouldBeSameAs(denied);
        var tlsError = Should.Throw<WorkspaceTrustException>(() => GrpcChannelFactory.Create("localhost:1",
            new GrpcChannelFactory.ChannelOptions { CaCertPath = missing, FileAccessGuard = Deny }));
        tlsError.ShouldBeSameAs(denied);
    }

    [Fact]
    public async Task Cached_request_descriptors_cannot_bypass_revocation()
    {
        var fixture = Path.Combine(AppContext.BaseDirectory, "TestProtosets", "test.protoset");
        _ = await Write(fixture);
        var store = Store();
        _ = await store.CommitOpenAsync(await Approve(store, WorkspacePath));
        var connection = store.Current.Connections[0];
        connection.Address = ""; // local descriptors only
        using var validator = new RequestValidator(new InvocationService(), fileAccess: new WorkspaceFileAccess(store));
        const string badJson = "{\"response_size\":\"not-a-number\"}";
        (await validator.ValidateAsync(connection, "testing.TestService/UnaryCall", badJson, true, TestContext.Current.CancellationToken)).ShouldNotBeEmpty();
        store.Trust.Revoke(WorkspacePath);
        // The advisory validator skips validation when schema authorization is unavailable, clearing its cache.
        (await validator.ValidateAsync(connection, "testing.TestService/UnaryCall", badJson, true, TestContext.Current.CancellationToken)).ShouldBeEmpty();
    }

    [Fact]
    public async Task Network_references_are_reported_without_probing_the_server()
    {
        if (!OperatingSystem.IsWindows()) { Assert.Skip("Windows UNC paths only."); return; }
        _ = await Write(@"\\approval-test.invalid\share\private.key");
        var prepared = await Store().PrepareAsync(WorkspacePath).WaitAsync(TimeSpan.FromSeconds(2));
        prepared.RequiresApproval.ShouldBeTrue();
        prepared.References[0].AbsolutePath.ShouldBe(@"\\approval-test.invalid\share\private.key");
    }

    [Fact]
    public async Task Windows_directory_junctions_cannot_escape_without_consent()
    {
        if (!OperatingSystem.IsWindows()) { Assert.Skip("Windows junctions only."); return; }
        _ = await Write("junction/api.protoset");
        var outside = Path.Combine(_root, "outside");
        _ = Directory.CreateDirectory(outside);
        await File.WriteAllTextAsync(Path.Combine(outside, "api.protoset"), "not read");
        var start = new System.Diagnostics.ProcessStartInfo("cmd.exe") { UseShellExecute = false, CreateNoWindow = true };
        foreach (var argument in new[] { "/c", "mklink", "/J", Path.Combine(_root, "project", "junction"), outside }) { start.ArgumentList.Add(argument); }
        using var process = System.Diagnostics.Process.Start(start)!;
        await process.WaitForExitAsync(TestContext.Current.CancellationToken);
        process.ExitCode.ShouldBe(0);
        var store = Store();
        (await store.PrepareAsync(WorkspacePath)).RequiresApproval.ShouldBeTrue();
        _ = await store.CommitOpenAsync(await Approve(store, WorkspacePath));
    }

    [Fact]
    public async Task Selective_revocation_accepts_the_selected_alias_and_retains_canonical_record_identity()
    {
        _ = await Write();
        var store = Store();
        _ = await store.CommitOpenAsync(await Approve(store, WorkspacePath));
        var alias = Path.Combine(_root, "selected-alias");
        _ = Directory.CreateSymbolicLink(alias, Path.GetDirectoryName(WorkspacePath)!);
        store.Trust.Revoke(Path.Combine(alias, "workspace.json"));
        store.Trust.ApprovedWorkspaces.ShouldBeEmpty();
        _ = Should.Throw<WorkspaceTrustException>(() => store.AuthorizeConnection(store.Current.Connections[0]));
    }
}
