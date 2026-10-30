using GrpCurl.Net.Studio.Services;
using GrpCurl.Net.Studio.Services.Secrets;
using GrpCurl.Net.Studio.TestSupport;
using GrpCurl.Net.Studio.ViewModels;
using GrpCurl.Net.Studio.ViewModels.Documents;
using GrpCurl.Net.Studio.ViewModels.Models.Connections;
using GrpCurl.Net.Studio.ViewModels.Panes;
using GrpCurl.Net.Studio.ViewModels.Services;

namespace GrpCurl.Net.Studio.Tests.Unit;

public sealed class WorkspaceImportFlowTests
{
    [Theory]
    [InlineData("unchecked")]
    [InlineData("subset")]
    [InlineData("all")]
    [InlineData("cancel")]
    [InlineData("source-change")]
    [InlineData("write-failure")]
    [InlineData("partial-persistence")]
    [InlineData("malformed")]
    public async Task Import_orders_file_approval_TLS_consent_and_secret_writes_and_reports_failures(string outcome)
    {
        var root = Path.Combine(Path.GetTempPath(), "grpcn-import-flow-" + Guid.NewGuid().ToString("N"));
        var source = Path.Combine(root, "source", "incoming.json");
        var active = Path.Combine(root, "active", "workspace.json");
        var ca = Path.Combine(root, "outside", "ca.pem");
        _ = Directory.CreateDirectory(Path.GetDirectoryName(source)!);
        _ = Directory.CreateDirectory(Path.GetDirectoryName(ca)!);
        await File.WriteAllTextAsync(ca, "Deliberately invalid certificate contents; import must only inspect the path.");
        var incoming = new WorkspaceModel
        {
            Connections = [new SavedConnection { Name = "API", TlsProfileId = "first" }],
            TlsProfiles =
            [
                new TlsProfile { Id = "first", Name = "Shared", InsecureSkipVerify = true, CaCertPath = "../outside/ca.pem", ClientCertPasswordSecretRef = "password" },
                new TlsProfile { Id = outcome == "malformed" ? "first" : "second", Name = "Shared", InsecureSkipVerify = true }
            ]
        };
        await File.WriteAllBytesAsync(source, WorkspaceSerializer.SerializeToUtf8(incoming));
        var sourceBytes = await File.ReadAllBytesAsync(source);
        var files = new SecureSecretFile((stage, _) =>
        {
            if (outcome == "partial-persistence" && stage == SecretFileStage.BeforeReplace) { throw new IOException("Injected trust persistence failure"); }
        });
        var store = new JsonWorkspaceStore(active, trust: new WorkspaceTrustStore(Path.Combine(root, "trust.json"), files));
        try
        {
            await store.SaveAsync(WorkspaceModel.Empty());
            var original = store.Current;
            var activeBytes = await File.ReadAllBytesAsync(active);
            var stages = new List<string>();
            var secrets = new FakeSecretStore();
            var dialogs = new FakeDialogService
            {
                OnShowDialog = dialog =>
                {
                    store.Current.ShouldBeSameAs(original);
                    secrets.ListAsync().GetAwaiter().GetResult().ShouldBeEmpty();
                    if (dialog is WorkspaceApprovalDialogViewModel approval)
                    {
                        stages.Add("files");
                        approval.Items.ShouldAllBe(i => !i.IsApproved);
                        return approval.Items.Select(i => i.Reference.ApprovalKey).ToArray();
                    }
                    if (dialog is WorkspaceImportDialogViewModel import)
                    {
                        stages.ShouldBe(["files"]);
                        stages.Add("tls");
                        import.TlsProfiles.ShouldAllBe(i => !i.DisableCertificateVerification);
                        import.Summary.SecretsToReenter.ShouldBe(1);
                        if (outcome == "cancel") { return null; }
                        if (outcome == "subset") { import.TlsProfiles[1].DisableCertificateVerification = true; }
                        if (outcome == "all") { foreach (var row in import.TlsProfiles) { row.DisableCertificateVerification = true; } }
                        return import.CreateDecision();
                    }
                    _ = dialog.ShouldBeOfType<ImportSecretsDialogViewModel>();
                    stages.ShouldBe(["files", "tls"]);
                    stages.Add("secrets");
                    if (outcome == "source-change") { File.AppendAllText(source, " "); }
                    if (outcome == "write-failure") { _ = Directory.CreateDirectory(active + ".tmp"); }
                    return new Dictionary<string, string> { ["password"] = "supplied" };
                }
            };
            var descriptors = new FakeDescriptorService();
            var selection = new ConnectionSelection();
            var theme = new FakeThemeService();
            var connections = new ConnectionsPaneViewModel(store, new FakeConnectionRegistry(), dialogs, selection);
            var documents = new DocumentsViewModel(descriptors, new ImmediateUiDispatcher(), new FakeClipboardService(),
                new FakeInvocationRunner(), dialogs, new FakeLauncherService(), new FakeRequestValidator(), new InMemorySettingsStore(), theme);
            var vm = new MainWindowViewModel(theme, connections,
                new ServiceExplorerViewModel(descriptors, selection, new FakeClipboardService(), new ImmediateUiDispatcher(), new FakeDocumentHost()),
                new ConsoleViewModel(), new InspectorViewModel(), documents, workspaceStore: store,
                session: new WorkspaceSessionViewModel(store, dialogs), filePicker: new FakeFilePickerService { OpenResult = source }, dialogs: dialogs, secrets: secrets);
            // Referenced certificate contents are unavailable to a .NET reader for the entire dialog/commit flow.
            using (var lockedCa = new FileStream(ca, FileMode.Open, FileAccess.Read, FileShare.None))
            {
                await vm.ImportWorkspaceCommand.ExecuteAsync(null);
            }
            descriptors.LoadCount.ShouldBe(0);
            dialogs.ConfirmCount.ShouldBe(0);
            var committed = outcome is "unchecked" or "subset" or "all" or "partial-persistence";
            if (committed)
            {
                store.Current.TlsProfiles.Count.ShouldBe(2);
                store.Current.TlsProfiles.Count(p => p.InsecureSkipVerify).ShouldBe(outcome == "all" ? 2 : outcome == "subset" ? 1 : 0);
                connections.Connections.Count.ShouldBe(1);
                if (outcome == "partial-persistence")
                {
                    dialogs.LastMessageBody.ShouldNotBeNull().ShouldContain("workspace was saved");
                    (await secrets.ListAsync()).ShouldBeEmpty();
                }
                else { (await secrets.GetAsync("password")).ShouldBe("supplied"); }
            }
            else
            {
                store.Current.ShouldBeSameAs(original);
                (await File.ReadAllBytesAsync(active)).ShouldBe(activeBytes);
                (await secrets.ListAsync()).ShouldBeEmpty();
                connections.Connections.ShouldBeEmpty();
                if (outcome == "malformed") { stages.ShouldBe(["files"]); }
                else if (outcome == "cancel") { stages.ShouldBe(["files", "tls"]); }
                if (outcome != "cancel") { dialogs.LastMessageBody.ShouldNotBeNullOrWhiteSpace(); }
            }
            if (outcome != "source-change") { (await File.ReadAllBytesAsync(source)).ShouldBe(sourceBytes); }
        }
        finally
        {
            store.ReleaseLock();
            Directory.Delete(root, true);
        }
    }
}
