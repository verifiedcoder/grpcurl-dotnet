using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Threading;
using Avalonia.VisualTree;
using GrpCurl.Net.Studio.Tests.UI.Headless;
using GrpCurl.Net.Studio.ViewModels;
using GrpCurl.Net.Studio.Views;

namespace GrpCurl.Net.Studio.Tests.UI;

/// <summary>L3 headless render for the command palette (Ctrl+K): items render and the controls are named.</summary>
public sealed class CommandPaletteUiTests(HeadlessSessionFixture fixture) : HeadlessTestBase(fixture)
{
    [Fact]
    public Task Palette_renders_its_items() => RunOnUiThread(() =>
    {
        var vm = new CommandPaletteViewModel(
        [
            new PaletteItem("New workspace", "Command", () => Task.CompletedTask),
            new PaletteItem("Go to connection: alpha", "Connection", () => Task.CompletedTask)
        ]);
        var window = new Window { Content = new CommandPaletteView { DataContext = vm }, DataContext = vm };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var texts = window.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).ToList();
        texts.ShouldContain("New workspace");
        texts.ShouldContain("Go to connection: alpha");

        var unnamed = window.GetVisualDescendants()
            .OfType<Control>()
            .Where(c => c is Button or ToggleButton or CheckBox or TextBox or ComboBox or ListBox)
            .Where(c => c.TemplatedParent is null)
            .Where(c => string.IsNullOrWhiteSpace(ControlAutomationPeer.CreatePeerForElement(c)?.GetName()))
            .Select(c => c.GetType().Name)
            .ToList();

        unnamed.ShouldBeEmpty("unnamed: " + string.Join(", ", unnamed));
    });
}
