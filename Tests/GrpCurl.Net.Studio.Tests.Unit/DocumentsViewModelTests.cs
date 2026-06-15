using GrpCurl.Net.Studio.Services;
using GrpCurl.Net.Studio.TestSupport;
using GrpCurl.Net.Studio.ViewModels.Documents;
using GrpCurl.Net.Studio.ViewModels.Models;
using GrpCurl.Net.Studio.ViewModels.Models.Connections;
using GrpCurl.Net.Studio.ViewModels.Models.Descriptors;

namespace GrpCurl.Net.Studio.Tests.Unit;

public sealed class DocumentsViewModelTests
{
    private static SavedConnection Conn() => new() { Name = "c", Address = "h:1" };

    private static DocumentsViewModel Create(InMemorySettingsStore? settings = null)
    {
        var descriptors = new FakeDescriptorService
        {
            OnDescribe = (_, symbol, _) => Task.FromResult(
                DescribeResult.Success(new MessageDescription(symbol, symbol, "f.proto", [], [], "{}")))
        };

        return new DocumentsViewModel(descriptors, new ImmediateUiDispatcher(), new FakeClipboardService(), new FakeInvocationRunner(), new FakeDialogService(), new FakeLauncherService(), new FakeRequestValidator(), settings ?? new InMemorySettingsStore(), new FakeThemeService());
    }

    [Fact]
    public void Open_invocation_seeds_network_defaults_and_dialect()
    {
        var settings = new InMemorySettingsStore();
        settings.Current.Network.DefaultDeadline = "30s";
        settings.Current.Network.MaxMessageSize = "8MB";
        settings.Current.General.CliShellDialect = ShellDialect.PowerShell;
        var docs = Create(settings);

        docs.OpenInvocation(Conn(), "pkg.Svc/Go");

        var tab = docs.Documents.OfType<InvocationDocumentViewModel>().ShouldHaveSingleItem();
        tab.Deadline.ShouldBe("30s");
        tab.MaxMessageSize.ShouldBe("8MB");
        tab.CliDialect.ShouldBe(ShellDialect.PowerShell);
    }

    [Fact]
    public void Open_settings_adds_a_single_settings_tab()
    {
        var docs = Create();

        docs.OpenSettings();
        docs.OpenSettings();

        docs.Documents.OfType<SettingsDocumentViewModel>().ShouldHaveSingleItem();
        docs.SelectedDocument.ShouldBeOfType<SettingsDocumentViewModel>();
    }

    [Fact]
    public void Open_describe_adds_a_tab_and_selects_it()
    {
        var docs = Create();

        docs.OpenDescribe(Conn(), "pkg.Alpha");

        docs.Documents.ShouldHaveSingleItem();
        docs.SelectedDocument.ShouldBe(docs.Documents[0]);
        ((DescribeDocumentViewModel)docs.Documents[0]).CurrentSymbol.ShouldBe("pkg.Alpha");
    }

    [Fact]
    public void Opening_the_same_symbol_selects_the_existing_tab()
    {
        var docs = Create();
        var connection = Conn();

        docs.OpenDescribe(connection, "pkg.Alpha");
        docs.OpenDescribe(connection, "pkg.Alpha");

        docs.Documents.ShouldHaveSingleItem();
    }

    [Fact]
    public void Opening_with_new_tab_creates_a_duplicate()
    {
        var docs = Create();
        var connection = Conn();

        docs.OpenDescribe(connection, "pkg.Alpha");
        docs.OpenDescribe(connection, "pkg.Alpha", newTab: true);

        docs.Documents.Count.ShouldBe(2);
    }

    [Fact]
    public void Closing_a_tab_removes_it_and_selects_a_neighbour()
    {
        var docs = Create();
        var connection = Conn();
        docs.OpenDescribe(connection, "pkg.Alpha");
        docs.OpenDescribe(connection, "pkg.Beta");
        var beta = docs.SelectedDocument!;

        beta.CloseCommand.Execute(null);

        docs.Documents.ShouldHaveSingleItem();
        ((DescribeDocumentViewModel)docs.Documents[0]).CurrentSymbol.ShouldBe("pkg.Alpha");
        docs.SelectedDocument.ShouldBe(docs.Documents[0]);
    }

    [Fact]
    public void Open_invocation_adds_a_new_invocation_tab()
    {
        var docs = Create();

        docs.OpenInvocation(Conn(), "pkg.Svc/Go", "{}");

        docs.Documents.ShouldHaveSingleItem().ShouldBeOfType<InvocationDocumentViewModel>();
        docs.SelectedDocument.ShouldBe(docs.Documents[0]);
    }

    [Fact]
    public void Closing_the_last_tab_clears_the_selection()
    {
        var docs = Create();
        docs.OpenDescribe(Conn(), "pkg.Alpha");

        docs.SelectedDocument!.CloseCommand.Execute(null);

        docs.Documents.ShouldBeEmpty();
        docs.SelectedDocument.ShouldBeNull();
    }
}
