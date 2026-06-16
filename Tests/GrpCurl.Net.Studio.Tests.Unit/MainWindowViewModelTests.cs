using GrpCurl.Net.Studio.Services;
using GrpCurl.Net.Studio.Tests.Unit.Fakes;
using GrpCurl.Net.Studio.TestSupport;
using GrpCurl.Net.Studio.ViewModels;
using GrpCurl.Net.Studio.ViewModels.Documents;
using GrpCurl.Net.Studio.ViewModels.Models;
using GrpCurl.Net.Studio.ViewModels.Models.Connections;
using GrpCurl.Net.Studio.ViewModels.Panes;
using GrpCurl.Net.Studio.ViewModels.Services;

namespace GrpCurl.Net.Studio.Tests.Unit;

public sealed class MainWindowViewModelTests
{
    /// <summary>A minimal document whose target connection the shell can inspect for the insecure banner.</summary>
    private sealed class StubDocument(SavedConnection? connection) : DocumentViewModel
    {
        public override SavedConnection? TabConnection => connection;
    }

    private static ConnectionsPaneViewModel EmptyConnectionsPane()
        => new(new FakeWorkspaceStore(), new FakeConnectionRegistry(), new FakeDialogService(), new ConnectionSelection());

    private static ServiceExplorerViewModel EmptyExplorer()
        => new(new FakeDescriptorService(), new ConnectionSelection(), new FakeClipboardService(), new ImmediateUiDispatcher(), new FakeDocumentHost());

    private static DocumentsViewModel EmptyDocuments()
        => new(new FakeDescriptorService(), new ImmediateUiDispatcher(), new FakeClipboardService(), new FakeInvocationRunner(),
            new FakeDialogService(), new FakeLauncherService(), new FakeRequestValidator(), new FakeSettingsStore(), new FakeThemeService());

    private static MainWindowViewModel CreateViewModel(FakeSettingsStore? settings = null)
        => new(
            new ThemeService(settings ?? new FakeSettingsStore()),
            EmptyConnectionsPane(),
            EmptyExplorer(),
            new ConsoleViewModel(),
            new InspectorViewModel(),
            EmptyDocuments());

    [Fact]
    public void Construction_exposes_title_and_default_pane_state()
    {
        var vm = CreateViewModel();

        vm.Title.ShouldNotBeNullOrWhiteSpace();
        vm.IsSidebarOpen.ShouldBeTrue();
        vm.IsInspectorOpen.ShouldBeTrue();
        vm.IsConsoleOpen.ShouldBeTrue();
        vm.IsFocusMode.ShouldBeFalse();
        vm.HasAnyConnection.ShouldBeFalse();
    }

    [Fact]
    public void Construction_reflects_persisted_theme()
    {
        var settings = new FakeSettingsStore();
        settings.Current.Appearance.Theme = "dark";

        var vm = CreateViewModel(settings);

        vm.SelectedTheme.ShouldBe(AppTheme.Dark);
    }

    [Fact]
    public void Toggle_sidebar_flips_state()
    {
        var vm = CreateViewModel();

        vm.ToggleSidebarCommand.Execute(null);

        vm.IsSidebarOpen.ShouldBeFalse();
    }

    [Fact]
    public void Focus_mode_collapses_all_panes_then_restores_prior_state()
    {
        var vm = CreateViewModel();

        vm.ToggleInspectorCommand.Execute(null); // inspector closed before focus mode
        vm.IsInspectorOpen.ShouldBeFalse();

        vm.ToggleFocusModeCommand.Execute(null);

        vm.IsFocusMode.ShouldBeTrue();
        vm.IsSidebarOpen.ShouldBeFalse();
        vm.IsInspectorOpen.ShouldBeFalse();
        vm.IsConsoleOpen.ShouldBeFalse();

        vm.ToggleFocusModeCommand.Execute(null);

        vm.IsFocusMode.ShouldBeFalse();
        vm.IsSidebarOpen.ShouldBeTrue();
        vm.IsInspectorOpen.ShouldBeFalse(); // restored to its pre-focus value
        vm.IsConsoleOpen.ShouldBeTrue();
    }

    [Fact]
    public async Task Set_theme_updates_selection_and_persists()
    {
        var settings = new FakeSettingsStore();
        var vm = CreateViewModel(settings);

        await vm.SetThemeCommand.ExecuteAsync(AppTheme.Dark);

        vm.SelectedTheme.ShouldBe(AppTheme.Dark);
        settings.Current.Appearance.Theme.ShouldBe("dark");
        settings.SaveCount.ShouldBe(1);
    }

    [Fact]
    public void Open_settings_opens_the_settings_tab()
    {
        var vm = CreateViewModel();

        vm.OpenSettingsCommand.Execute(null);

        vm.Documents.Documents.OfType<SettingsDocumentViewModel>().ShouldHaveSingleItem();
    }

    // ── SEC-014 insecure-skip-verify banner ──────────────────────────────────

    private static MainWindowViewModel CreateWithProfiles(out DocumentsViewModel documents, out ITlsProfileStore store, WorkspaceModel workspace)
    {
        documents = EmptyDocuments();
        store = new TlsProfileStore(new FakeWorkspaceStore(workspace), new FakeSecretStore());

        return new MainWindowViewModel(
            new ThemeService(new FakeSettingsStore()),
            EmptyConnectionsPane(), EmptyExplorer(), new ConsoleViewModel(), new InspectorViewModel(), documents, store);
    }

    [Fact]
    public void Banner_is_hidden_with_no_open_tabs()
    {
        var vm = CreateViewModel();

        vm.IsInsecureBannerVisible.ShouldBeFalse();
    }

    [Fact]
    public void Opening_a_tab_on_an_insecure_connection_shows_the_banner()
    {
        var profile = new TlsProfile { Name = "danger", InsecureSkipVerify = true };
        var connection = new SavedConnection { Name = "prod-debug", Transport = TransportMode.Tls, TlsProfileId = profile.Id };
        var vm = CreateWithProfiles(out var documents, out _, new WorkspaceModel { TlsProfiles = [profile] });

        documents.Documents.Add(new StubDocument(connection));

        vm.IsInsecureBannerVisible.ShouldBeTrue();
        vm.InsecureBannerText.ShouldContain("prod-debug");
        vm.ReviewInsecureConnectionCommand.CanExecute(null).ShouldBeTrue();
    }

    [Fact]
    public void Closing_the_insecure_tab_hides_the_banner()
    {
        var profile = new TlsProfile { Name = "danger", InsecureSkipVerify = true };
        var connection = new SavedConnection { Name = "prod-debug", Transport = TransportMode.Tls, TlsProfileId = profile.Id };
        var vm = CreateWithProfiles(out var documents, out _, new WorkspaceModel { TlsProfiles = [profile] });
        var doc = new StubDocument(connection);
        documents.Documents.Add(doc);

        documents.Documents.Remove(doc);

        vm.IsInsecureBannerVisible.ShouldBeFalse();
    }

    [Fact]
    public void A_system_default_tls_tab_does_not_show_the_banner()
    {
        var profile = new TlsProfile { Name = "safe", InsecureSkipVerify = false };
        var connection = new SavedConnection { Name = "prod", Transport = TransportMode.Tls, TlsProfileId = profile.Id };
        var vm = CreateWithProfiles(out var documents, out _, new WorkspaceModel { TlsProfiles = [profile] });

        documents.Documents.Add(new StubDocument(connection));

        vm.IsInsecureBannerVisible.ShouldBeFalse();
    }

    [Fact]
    public void A_plaintext_tab_never_shows_the_banner()
    {
        var profile = new TlsProfile { Name = "danger", InsecureSkipVerify = true };
        var connection = new SavedConnection { Name = "p", Transport = TransportMode.Plaintext, TlsProfileId = profile.Id };
        var vm = CreateWithProfiles(out var documents, out _, new WorkspaceModel { TlsProfiles = [profile] });

        documents.Documents.Add(new StubDocument(connection));

        vm.IsInsecureBannerVisible.ShouldBeFalse();
    }
}
