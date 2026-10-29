using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using GrpCurl.Net.Studio.Services;
using GrpCurl.Net.Studio.Tests.UI.Headless;
using GrpCurl.Net.Studio.TestSupport;
using GrpCurl.Net.Studio.ViewModels.Documents;
using GrpCurl.Net.Studio.ViewModels.Models.Connections;
using GrpCurl.Net.Studio.ViewModels.Models.History;
using GrpCurl.Net.Studio.Views.Documents;

namespace GrpCurl.Net.Studio.Tests.UI;

/// <summary>L3 headless render for the History tab (FR-122): the grid + toolbar render with entries.</summary>
public sealed class HistoryDocumentUiTests(HeadlessSessionFixture fixture) : HeadlessTestBase(fixture)
{
    private static HistoryEntry Entry(string id, string method) => new(
        HistoryEntry.CurrentVersion, id, DateTimeOffset.UtcNow, HistoryKind.Grpc,
        new HistoryConnection("staging", "h:1", "tls", null), null, method,
        new HistoryRequest("json", "{}", false, [], "10s", false, false, null, null, null),
        new HistoryOutcome("OK", "success", 0, 12, 1, 1, null, false, null));

    [Fact]
    public Task History_grid_renders_rows_and_the_toolbar() => RunOnUiThread(() =>
    {
        var store = new FakeHistoryStore();
        store.Entries.Add(Entry("e1", "pkg.Svc/First"));
        store.Entries.Add(Entry("e2", "pkg.Svc/Second"));
        var vm = new HistoryDocumentViewModel(
            store, new InMemorySettingsStore(),
            new FakeWorkspaceStore(new WorkspaceModel { Connections = [new SavedConnection { Name = "staging", Address = "h:1" }] }),
            new FakeDocumentHost(), new FakeDialogService(), new ImmediateUiDispatcher(), new FakeFilePickerService());
        vm.LoadAsync().GetAwaiter().GetResult();

        var window = new Window { Content = new HistoryDocumentView { DataContext = vm }, Width = 900, Height = 500 };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var list = window.GetVisualDescendants().OfType<ListBox>()
            .Single(l => Equals(l.GetValue(Avalonia.Automation.AutomationProperties.NameProperty), "History entries"));
        list.GetVisualDescendants().OfType<ListBoxItem>().ShouldNotBeEmpty();
        window.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).ShouldContain("pkg.Svc/First");
    });
    [Fact]
    public Task Missing_key_renders_recovery_message_and_clear_all_remains_available() => RunOnUiThread(() =>
    {
        var directory = Path.Combine(Path.GetTempPath(), "grpcn-history-ui-" + Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "history.ndjson");
        var secrets = new FakeSecretStore();
        using var store = new JsonHistoryStore(path, secrets);
        store.AppendAsync(Entry("e1", "Svc/Call")).GetAwaiter().GetResult();
        secrets.DeleteAsync(HistoryCipher.KeyReference).GetAwaiter().GetResult();
        var before = File.ReadAllText(path);
        var dialogs = new FakeDialogService { ConfirmResult = true };
        var vm = new HistoryDocumentViewModel(store, new InMemorySettingsStore(), new FakeWorkspaceStore(),
            new FakeDocumentHost(), dialogs, new ImmediateUiDispatcher());
        vm.LoadAsync().GetAwaiter().GetResult();
        var window = new Window { Content = new HistoryDocumentView { DataContext = vm }, Width = 900, Height = 500 };
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            var status = window.GetVisualDescendants().OfType<TextBlock>().Single(t =>
                Equals(t.GetValue(Avalonia.Automation.AutomationProperties.NameProperty), "History storage status"));
            status.Text.ShouldNotBeNull().ShouldContain("Restore the original keystore");
            vm.Rows.ShouldBeEmpty();
            File.ReadAllText(path).ShouldBe(before);
            var clear = window.GetVisualDescendants().OfType<Button>().Single(b =>
                Equals(b.GetValue(Avalonia.Automation.AutomationProperties.NameProperty), "Clear history"));
            clear.IsEnabled.ShouldBeTrue();
            clear.Flyout!.ShowAt(clear);
            Dispatcher.UIThread.RunJobs();
            var all = ((MenuFlyout)clear.Flyout).Items.OfType<MenuItem>().Single(i => Equals(i.Header, "Clear all…"));
            all.Command!.Execute(null);
            Dispatcher.UIThread.RunJobs();
            File.Exists(path).ShouldBeFalse();
            dialogs.ConfirmCount.ShouldBe(1);
        }
        finally { window.Close(); Directory.Delete(directory, true); }
    });

}
