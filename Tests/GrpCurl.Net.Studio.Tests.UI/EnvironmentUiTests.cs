using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Threading;
using Avalonia.VisualTree;
using GrpCurl.Net.Studio.Services;
using GrpCurl.Net.Studio.Tests.UI.Headless;
using GrpCurl.Net.Studio.TestSupport;
using GrpCurl.Net.Studio.ViewModels.Connections;
using GrpCurl.Net.Studio.ViewModels.Models.Connections;

namespace GrpCurl.Net.Studio.Tests.UI;

/// <summary>
///     L3 headless render tests for the environment manager + editor (E3.2 PR-B): environments render as
///     rows, the editor shows its variables, and every interactive control carries an accessible name
///     (SPEC-020 §6).
/// </summary>
public sealed class EnvironmentUiTests(HeadlessSessionFixture fixture) : HeadlessTestBase(fixture)
{
    private static EnvironmentManagerViewModel ManagerVm(params WorkspaceEnvironment[] environments)
    {
        var store = new EnvironmentStore(
            new FakeWorkspaceStore(new WorkspaceModel { Environments = [.. environments] }), new FakeSecretStore());
        return new EnvironmentManagerViewModel(store, new FakeDialogService(), new FakeSecretStore());
    }

    [Fact]
    public Task Environments_render_as_rows() => RunOnUiThread(() =>
    {
        var vm = ManagerVm(new WorkspaceEnvironment { Id = "e1", Name = "staging" });
        var window = new Window { Content = new Views.Connections.EnvironmentManagerView { DataContext = vm }, DataContext = vm };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var texts = window.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).ToList();
        texts.ShouldContain("staging");
    });

    [Fact]
    public Task Editor_renders_its_variables() => RunOnUiThread(() =>
    {
        var env = new WorkspaceEnvironment
        {
            Id = "e1", Name = "staging",
            Variables =
            [
                new EnvironmentVariable { Name = "HOST", Value = StringOrSecret.Plain("api:443") },
                new EnvironmentVariable { Name = "TOKEN", Value = StringOrSecret.Secret("ref-1") }
            ]
        };
        var vm = new EnvironmentEditorViewModel(new FakeSecretStore(), env);
        var window = new Window { Content = new Views.Connections.EnvironmentEditorView { DataContext = vm }, DataContext = vm };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var values = window.GetVisualDescendants().OfType<TextBox>().Select(t => t.Text).ToList();
        values.ShouldContain("staging");
        values.ShouldContain("HOST");
        values.ShouldContain("api:443");
        values.ShouldContain("TOKEN");
    });

    [Fact]
    public Task Editor_controls_all_have_accessible_names() => RunOnUiThread(() =>
    {
        var env = new WorkspaceEnvironment
        {
            Id = "e1", Name = "staging",
            Variables = [new EnvironmentVariable { Name = "HOST", Value = StringOrSecret.Plain("api:443") }]
        };
        var vm = new EnvironmentEditorViewModel(new FakeSecretStore(), env);
        var window = new Window { Content = new Views.Connections.EnvironmentEditorView { DataContext = vm }, DataContext = vm };
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
