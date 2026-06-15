using GrpCurl.Net.Studio.TestSupport;
using GrpCurl.Net.Studio.ViewModels.Documents;
using GrpCurl.Net.Studio.ViewModels.Models.Connections;
using GrpCurl.Net.Studio.ViewModels.Models.Descriptors;

namespace GrpCurl.Net.Studio.Tests.Unit;

public sealed class DocumentsViewModelTests
{
    private static SavedConnection Conn() => new() { Name = "c", Address = "h:1" };

    private static DocumentsViewModel Create()
    {
        var descriptors = new FakeDescriptorService
        {
            OnDescribe = (_, symbol, _) => Task.FromResult(
                DescribeResult.Success(new MessageDescription(symbol, symbol, "f.proto", [], [], "{}")))
        };

        return new DocumentsViewModel(descriptors, new ImmediateUiDispatcher(), new FakeClipboardService());
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
    public void Closing_the_last_tab_clears_the_selection()
    {
        var docs = Create();
        docs.OpenDescribe(Conn(), "pkg.Alpha");

        docs.SelectedDocument!.CloseCommand.Execute(null);

        docs.Documents.ShouldBeEmpty();
        docs.SelectedDocument.ShouldBeNull();
    }
}
