using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Threading;
using Avalonia.VisualTree;
using GrpCurl.Net.Studio.Services;
using GrpCurl.Net.Studio.TestSupport;
using GrpCurl.Net.Studio.Tests.UI.Headless;
using GrpCurl.Net.Studio.ViewModels.Connections;
using GrpCurl.Net.Studio.ViewModels.Models.Connections;

namespace GrpCurl.Net.Studio.Tests.UI;

/// <summary>
///     L3 headless render tests for the TLS profile manager (E2.2 PR-D): profiles render as rows with
///     their actions, and every interactive control carries an accessible name (SPEC-020 §6).
/// </summary>
public sealed class TlsProfileManagerUiTests(HeadlessSessionFixture fixture) : HeadlessTestBase(fixture)
{
    private static TlsProfileManagerViewModel CreateViewModel(params TlsProfile[] profiles)
    {
        var store = new TlsProfileStore(
            new FakeWorkspaceStore(new WorkspaceModel { TlsProfiles = [.. profiles] }), new FakeSecretStore());
        return new TlsProfileManagerViewModel(store, new FakeFilePickerService(), new FakeDialogService(), new FakeSecretStore());
    }

    [Fact]
    public Task Profiles_render_as_rows() => RunOnUiThread(() =>
    {
        var vm = CreateViewModel(new TlsProfile { Name = "mtls-prod" });
        var window = new Window { Content = new Views.Connections.TlsProfileManagerView { DataContext = vm }, DataContext = vm };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var texts = window.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).ToList();
        texts.ShouldContain("mtls-prod");
    });

    [Fact]
    public Task Every_interactive_control_has_an_accessible_name() => RunOnUiThread(() =>
    {
        var vm = CreateViewModel(new TlsProfile { Name = "mtls-prod" });
        var window = new Window { Content = new Views.Connections.TlsProfileManagerView { DataContext = vm }, DataContext = vm };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var unnamed = window.GetVisualDescendants()
            .OfType<Control>()
            .Where(c => c is Button or ToggleButton or CheckBox or TextBox or ComboBox)
            .Where(c => c.TemplatedParent is null)
            .Where(c => string.IsNullOrWhiteSpace(ControlAutomationPeer.CreatePeerForElement(c)?.GetName()))
            .Select(c => c.GetType().Name)
            .ToList();

        unnamed.ShouldBeEmpty("unnamed: " + string.Join(", ", unnamed));
    });
}
