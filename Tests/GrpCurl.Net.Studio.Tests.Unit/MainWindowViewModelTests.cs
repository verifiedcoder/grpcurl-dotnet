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

    // ── Welcome overlay must not hide a document opened before the first connection ──

    [Fact]
    public void Welcome_shows_when_there_are_no_connections_and_no_documents()
    {
        var vm = CreateViewModel();

        vm.HasAnyConnection.ShouldBeFalse();
        vm.ShowWelcome.ShouldBeTrue();
        vm.ShowDocuments.ShouldBeFalse();
    }

    [Fact]
    public void Opening_a_document_without_connections_reveals_the_document_area()
    {
        var vm = CreateViewModel();
        vm.ShowDocuments.ShouldBeFalse();

        // File → Settings on a fresh, connection-less workspace opens a Settings tab.
        vm.Documents.OpenSettings();

        vm.Documents.Documents.ShouldNotBeEmpty();
        vm.ShowDocuments.ShouldBeTrue();  // the tab area is now shown…
        vm.ShowWelcome.ShouldBeFalse();   // …and the welcome overlay steps aside
    }

    [Fact]
    public void Closing_the_last_document_without_connections_restores_the_welcome()
    {
        var vm = CreateViewModel();
        vm.Documents.OpenSettings();
        vm.ShowWelcome.ShouldBeFalse();

        vm.Documents.Documents.Clear();

        vm.ShowWelcome.ShouldBeTrue();
    }

    // ── E3.1 PR-D: File-menu workspace operations ────────────────────────────

    private static MainWindowViewModel CreateWithWorkspace(
        out FakeWorkspaceStore store, out FakeFilePickerService picker, out FakeDialogService dialogs,
        out ConnectionsPaneViewModel connections, out DocumentsViewModel documents)
    {
        store = new FakeWorkspaceStore(new WorkspaceModel
        {
            Id = "w1", Name = "Demo",
            Connections = [new SavedConnection { Name = "a", Address = "h:1" }]
        });
        picker = new FakeFilePickerService();
        dialogs = new FakeDialogService();
        var session = new WorkspaceSessionViewModel(store, dialogs);
        connections = new ConnectionsPaneViewModel(store, new FakeConnectionRegistry(), dialogs, new ConnectionSelection());
        documents = EmptyDocuments();
        return new MainWindowViewModel(
            new ThemeService(new FakeSettingsStore()), connections, EmptyExplorer(), new ConsoleViewModel(),
            new InspectorViewModel(), documents, profileStore: null,
            workspaceStore: store, session: session, filePicker: picker, dialogs: dialogs);
    }

    [Fact]
    public void Recent_workspaces_are_exposed_from_the_store()
    {
        var vm = CreateWithWorkspace(out var store, out _, out _, out _, out _);
        store.SeedRecent("/a/one.gcnws.json", "/b/two.gcnws.json");

        // RefreshRecents runs in the ctor; re-create to pick up the seeded list.
        vm = new MainWindowViewModel(
            new ThemeService(new FakeSettingsStore()),
            new ConnectionsPaneViewModel(store, new FakeConnectionRegistry(), new FakeDialogService(), new ConnectionSelection()),
            EmptyExplorer(), new ConsoleViewModel(), new InspectorViewModel(), EmptyDocuments(),
            workspaceStore: store, session: new WorkspaceSessionViewModel(store, new FakeDialogService()),
            filePicker: new FakeFilePickerService(), dialogs: new FakeDialogService());

        vm.RecentWorkspaces.Count.ShouldBe(2);
        vm.HasRecentWorkspaces.ShouldBeTrue();
    }

    [Fact]
    public async Task New_workspace_switches_and_closes_open_tabs()
    {
        var vm = CreateWithWorkspace(out var store, out _, out _, out _, out var documents);
        documents.OpenSettings();
        documents.Documents.ShouldNotBeEmpty();

        await vm.NewWorkspaceCommand.ExecuteAsync(null);

        store.CurrentPath.ShouldBeNull();          // untitled
        documents.Documents.ShouldBeEmpty();        // tabs closed on switch
    }

    [Fact]
    public async Task Open_workspace_loads_the_picked_file_and_reloads_connections()
    {
        var vm = CreateWithWorkspace(out var store, out var picker, out _, out var connections, out _);
        connections.Connections.Count.ShouldBe(1); // the seed connection
        picker.OpenResult = "/ws/other.gcnws.json";
        store.OpenResult = new WorkspaceModel
        {
            Id = "w2", Name = "Other",
            Connections = [new SavedConnection { Name = "x", Address = "h:9" }, new SavedConnection { Name = "y", Address = "h:8" }]
        };

        await vm.OpenWorkspaceCommand.ExecuteAsync(null);

        store.CurrentPath.ShouldBe("/ws/other.gcnws.json");
        connections.Connections.Count.ShouldBe(2); // reloaded from the opened workspace
    }

    [Fact]
    public async Task Open_workspace_reports_a_schema_error_and_does_not_switch()
    {
        var vm = CreateWithWorkspace(out var store, out var picker, out var dialogs, out var connections, out _);
        picker.OpenResult = "/ws/newer.gcnws.json";
        store.OpenError = WorkspaceSchemaException.NewerVersion(2, 1);

        await vm.OpenWorkspaceCommand.ExecuteAsync(null);

        dialogs.MessageCount.ShouldBe(1);
        dialogs.LastMessageTitle.ShouldBe("Could not open workspace");
        connections.Connections.Count.ShouldBe(1); // unchanged
    }

    [Fact]
    public async Task Save_when_untitled_routes_to_save_as()
    {
        var vm = CreateWithWorkspace(out var store, out var picker, out _, out _, out _);
        store.NewWorkspace(); // CurrentPath becomes null
        picker.SaveResult = "/ws/named.gcnws.json";

        await vm.SaveWorkspaceCommand.ExecuteAsync(null);

        store.LastSavedAsPath.ShouldBe("/ws/named.gcnws.json");
    }

    [Fact]
    public async Task Save_when_titled_flushes_through_the_store()
    {
        var vm = CreateWithWorkspace(out var store, out _, out _, out _, out _);
        await store.SaveAsAsync(store.Current, "/ws/demo.gcnws.json", TestContext.Current.CancellationToken); // give it a path

        await vm.SaveWorkspaceCommand.ExecuteAsync(null);

        store.SaveNowCount.ShouldBe(1);
    }

    [Fact]
    public async Task Reload_refreshes_the_connection_list()
    {
        var vm = CreateWithWorkspace(out var store, out _, out _, out var connections, out _);
        await store.SaveAsAsync(store.Current, "/ws/demo.gcnws.json", TestContext.Current.CancellationToken);
        store.ReloadResult = new WorkspaceModel { Id = "w1", Name = "Demo", Connections = [] }; // on-disk has none

        await vm.ReloadWorkspaceCommand.ExecuteAsync(null);

        store.ReloadCount.ShouldBe(1);
        connections.Connections.ShouldBeEmpty();
    }

    [Fact]
    public async Task New_with_unsaved_changes_is_cancellable()
    {
        var vm = CreateWithWorkspace(out var store, out _, out var dialogs, out _, out _);
        store.SetDirty(true);
        dialogs.ConfirmResult = false; // user keeps the current workspace

        await vm.NewWorkspaceCommand.ExecuteAsync(null);

        dialogs.ConfirmCount.ShouldBe(1);
        store.Current.Name.ShouldBe("Demo"); // not replaced
    }

    [Fact]
    public void Title_reflects_the_workspace_name_and_dirty_state()
    {
        var vm = CreateWithWorkspace(out var store, out _, out _, out _, out _);

        vm.Title.ShouldContain("Demo");
        vm.Title.ShouldNotContain("●");

        store.SetDirty(true);

        vm.Title.ShouldContain("●");
    }
}
