using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using GrpCurl.Net.Studio.Services;
using GrpCurl.Net.Studio.Tests.UI.Headless;
using GrpCurl.Net.Studio.TestSupport;
using GrpCurl.Net.Studio.ViewModels.Models.Connections;
using GrpCurl.Net.Studio.ViewModels.Panes;
using GrpCurl.Net.Studio.ViewModels.Services;
using GrpCurl.Net.Studio.Views.Panes;

namespace GrpCurl.Net.Studio.Tests.UI;

/// <summary>L3 headless render for the saved-request sidebar (FR-145): requests nest under their connection.</summary>
public sealed class SavedRequestSidebarUiTests(HeadlessSessionFixture fixture) : HeadlessTestBase(fixture)
{
    [Fact]
    public Task Saved_requests_render_nested_under_their_connection() => RunOnUiThread(() =>
    {
        var workspace = new FakeWorkspaceStore(new WorkspaceModel
        {
            Connections = [new SavedConnection { Id = "c1", Name = "alpha", Address = "h:1" }],
            SavedRequests = [new SavedRequest { Id = "r1", Name = "say hello", ConnectionId = "c1", Method = "p.S/Hello" }]
        });
        var pane = new ConnectionsPaneViewModel(
            workspace, new FakeConnectionRegistry(), new FakeDialogService(), new ConnectionSelection(),
            savedRequests: new SavedRequestStore(workspace), documentHost: new FakeDocumentHost());

        var window = new Window { Content = new ConnectionsPaneView { DataContext = pane }, Width = 300, Height = 480 };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var texts = window.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).ToList();
        texts.ShouldContain("alpha");      // the connection
        texts.ShouldContain("say hello");  // its nested saved request

        // The open affordance is a named, command-bound control.
        window.GetVisualDescendants().OfType<Button>()
            .Any(b => Equals(b.GetValue(Avalonia.Automation.AutomationProperties.NameProperty), "Open saved request"))
            .ShouldBeTrue();
    });
}
