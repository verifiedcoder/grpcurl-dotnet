using GrpCurl.Net.Studio.Services;
using GrpCurl.Net.Studio.TestSupport;
using GrpCurl.Net.Studio.ViewModels;
using GrpCurl.Net.Studio.ViewModels.Documents;
using GrpCurl.Net.Studio.ViewModels.Models.Connections;
using GrpCurl.Net.Studio.ViewModels.Panes;
using GrpCurl.Net.Studio.ViewModels.Services;

namespace GrpCurl.Net.Studio.Tests.Unit;

public sealed class WorkspaceApprovalFlowTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "grpcn-approval-flow-" + Guid.NewGuid().ToString("N"));
    private JsonWorkspaceStore? _store;
    public void Dispose()
    {
        _store?.ReleaseLock();
        if (Directory.Exists(_root)) { Directory.Delete(_root, true); }
    }

    [Theory]
    [InlineData("open", false)]
    [InlineData("recent", false)]
    [InlineData("reload", false)]
    [InlineData("import", false)]
    [InlineData("startup", false)]
    [InlineData("open", true)]
    [InlineData("recent", true)]
    [InlineData("reload", true)]
    [InlineData("import", true)]
    [InlineData("startup", true)]
    public async Task Every_entry_point_requires_consent_without_premature_file_or_secret_access(string operation, bool consent)
    {
        var selected = Path.Combine(_root, "selected", "workspace.json");
        var active = Path.Combine(_root, "active", "workspace.json");
        _ = Directory.CreateDirectory(Path.GetDirectoryName(selected)!);
        var incoming = WorkspaceModel.Empty();
        incoming.Name = "Incoming";
        incoming.Connections.Add(new SavedConnection
        {
            Id = "incoming", Name = "Untrusted connection", TlsProfileId = "tls",
            DescriptorSource = new DescriptorSourceConfig { ProtosetPaths = ["../external/schema.protoset"] }
        });
        incoming.TlsProfiles.Add(new TlsProfile
        {
            Id = "tls", Name = "TLS", ClientKeyPath = "../external/private.key", ClientCertPasswordSecretRef = "imported-secret"
        });
        await File.WriteAllBytesAsync(selected, WorkspaceSerializer.SerializeToUtf8(incoming));
        var before = await File.ReadAllBytesAsync(selected);
        var store = new JsonWorkspaceStore(operation == "startup" ? selected : active,
            trust: new WorkspaceTrustStore(Path.Combine(_root, "private", "workspace-trust.json")));
        _store = store;
        if (operation == "startup") { _ = await store.LoadAsync(); }
        else
        {
            await store.SaveAsync(WorkspaceModel.Empty());
            if (operation == "reload")
            {
                File.Copy(selected, active, true);
                selected = active;
            }
        }
        var original = store.Current;
        var originalPath = store.CurrentPath;
        var originalRecents = store.RecentWorkspaces.Select(r => r.Path).ToArray();
        var approvalCount = 0;
        var secrets = new FakeSecretStore();
        var dialogs = new FakeDialogService
        {
            ConfirmResult = true,
            OnShowDialog = dialog =>
            {
                if (dialog is WorkspaceApprovalDialogViewModel approval)
                {
                    approvalCount++;
                    approval.Items.ShouldAllBe(i => !i.IsApproved);
                    store.Current.ShouldBeSameAs(original);
                    store.CurrentPath.ShouldBe(originalPath);
                    return consent ? approval.Items.Select(i => i.Reference.ApprovalKey).ToArray() : null;
                }
                return dialog is ImportSecretsDialogViewModel ? new Dictionary<string, string> { ["imported-secret"] = "supplied" } : null;
            }
        };
        var descriptors = new FakeDescriptorService();
        var selection = new ConnectionSelection();
        var picker = new FakeFilePickerService { OpenResult = selected };
        var settings = new InMemorySettingsStore();
        var theme = new FakeThemeService();
        var documents = new DocumentsViewModel(descriptors, new ImmediateUiDispatcher(), new FakeClipboardService(),
            new FakeInvocationRunner(), dialogs, new FakeLauncherService(), new FakeRequestValidator(), settings, theme);
        var vm = new MainWindowViewModel(theme,
            new ConnectionsPaneViewModel(store, new FakeConnectionRegistry(), dialogs, selection),
            new ServiceExplorerViewModel(descriptors, selection, new FakeClipboardService(), new ImmediateUiDispatcher(), new FakeDocumentHost()),
            new ConsoleViewModel(), new InspectorViewModel(), documents, workspaceStore: store,
            session: new WorkspaceSessionViewModel(store, dialogs), filePicker: picker, dialogs: dialogs, secrets: secrets);
        switch (operation)
        {
            case "open": await vm.OpenWorkspaceCommand.ExecuteAsync(null); break;
            case "recent": await vm.OpenRecentCommand.ExecuteAsync(selected); break;
            case "reload": await vm.ReloadWorkspaceCommand.ExecuteAsync(null); break;
            case "import": await vm.ImportWorkspaceCommand.ExecuteAsync(null); break;
            case "startup": (await vm.CompleteStartupAsync()).ShouldBe(consent); break;
        }
        approvalCount.ShouldBeGreaterThan(0);
        descriptors.LoadCount.ShouldBe(0);
        if (consent)
        {
            store.Current.Connections.Count.ShouldBe(1);
            if (operation == "import") { (await secrets.GetAsync("imported-secret")).ShouldBe("supplied"); }
        }
        else
        {
            store.Current.ShouldBeSameAs(original);
            store.CurrentPath.ShouldBe(originalPath);
            store.RecentWorkspaces.Select(r => r.Path).ShouldBe(originalRecents);
            (await secrets.ListAsync()).ShouldBeEmpty();
        }
        (await File.ReadAllBytesAsync(selected)).ShouldBe(before);
        File.Exists(selected + ".corrupt").ShouldBeFalse();
    }

    [Fact]
    public async Task Reload_approval_failure_keeps_existing_edits_and_reports_actionable_error()
    {
        _ = Directory.CreateDirectory(_root);
        var path = Path.Combine(_root, "workspace.json");
        var model = WorkspaceModel.Empty();
        await File.WriteAllBytesAsync(path, WorkspaceSerializer.SerializeToUtf8(model));
        var store = new JsonWorkspaceStore(path);
        _store = store;
        _ = await store.OpenAsync(path);
        store.Current.Name = "unsaved edit";
        model.Connections.Add(new SavedConnection { DescriptorSource = new DescriptorSourceConfig { ProtosetPaths = ["../outside"] } });
        await File.WriteAllBytesAsync(path, WorkspaceSerializer.SerializeToUtf8(model));
        var dialogs = new FakeDialogService
        {
            ConfirmResult = true,
            OnShowDialog = dialog =>
            {
                File.AppendAllText(path, " ");
                return ((WorkspaceApprovalDialogViewModel)dialog).Items.Select(i => i.Reference.ApprovalKey).ToArray();
            }
        };
        (await new WorkspaceSessionViewModel(store, dialogs).ReloadAsync()).ShouldBeFalse();
        store.Current.Name.ShouldBe("unsaved edit");
        dialogs.LastMessageBody.ShouldNotBeNull().ShouldContain("changed during review");
    }
}
