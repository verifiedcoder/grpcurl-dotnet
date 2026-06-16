using Avalonia.Controls;
using Avalonia.Threading;
using GrpCurl.Net.Studio.Services;
using GrpCurl.Net.Studio.TestSupport;
using GrpCurl.Net.Studio.Tests.UI.Headless;
using GrpCurl.Net.Studio.ViewModels;
using GrpCurl.Net.Studio.ViewModels.Documents;
using GrpCurl.Net.Studio.ViewModels.Models.Connections;
using GrpCurl.Net.Studio.ViewModels.Panes;
using GrpCurl.Net.Studio.ViewModels.Services;
using GrpCurl.Net.Studio.Views;

namespace GrpCurl.Net.Studio.Tests.UI;

/// <summary>
///     L3 render tests for the SEC-014 insecure-skip-verify banner: the danger banner (and its
///     <c>Banner.DangerBg</c> token) renders below the menu bar when an open tab uses a skip-verify
///     profile, and is hidden otherwise.
/// </summary>
public sealed class InsecureBannerUiTests(HeadlessSessionFixture fixture) : HeadlessTestBase(fixture)
{
    private sealed class StubDocument(SavedConnection? connection) : DocumentViewModel
    {
        public override SavedConnection? TabConnection => connection;
    }

    private static (MainWindowViewModel Vm, DocumentsViewModel Documents) CreateShell(WorkspaceModel workspace)
    {
        var documents = new DocumentsViewModel(
            new FakeDescriptorService(), new ImmediateUiDispatcher(), new FakeClipboardService(), new FakeInvocationRunner(),
            new FakeDialogService(), new FakeLauncherService(), new FakeRequestValidator(), new InMemorySettingsStore(), new FakeThemeService());

        var store = new TlsProfileStore(new FakeWorkspaceStore(workspace), new FakeSecretStore());

        var vm = new MainWindowViewModel(
            new FakeThemeService(),
            new ConnectionsPaneViewModel(new FakeWorkspaceStore(), new FakeConnectionRegistry(), new FakeDialogService(), new ConnectionSelection()),
            new ServiceExplorerViewModel(new FakeDescriptorService(), new ConnectionSelection(), new FakeClipboardService(), new ImmediateUiDispatcher(), new FakeDocumentHost()),
            new ConsoleViewModel(), new InspectorViewModel(), documents, store);

        return (vm, documents);
    }

    [Fact]
    public Task Banner_is_hidden_by_default() => RunOnUiThread(() =>
    {
        var (vm, _) = CreateShell(new WorkspaceModel());
        var window = new MainWindow { DataContext = vm };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        window.FindControl<Control>("InsecureBanner")!.IsEffectivelyVisible.ShouldBeFalse();
    });

    [Fact]
    public Task Banner_shows_when_an_insecure_tab_is_open() => RunOnUiThread(() =>
    {
        var profile = new TlsProfile { Name = "danger", InsecureSkipVerify = true };
        var connection = new SavedConnection { Name = "prod-debug", Transport = TransportMode.Tls, TlsProfileId = profile.Id };
        var (vm, documents) = CreateShell(new WorkspaceModel { TlsProfiles = [profile] });

        var window = new MainWindow { DataContext = vm };
        window.Show();
        documents.Documents.Add(new StubDocument(connection));
        Dispatcher.UIThread.RunJobs();

        window.FindControl<Control>("InsecureBanner")!.IsEffectivelyVisible.ShouldBeTrue();
    });
}
