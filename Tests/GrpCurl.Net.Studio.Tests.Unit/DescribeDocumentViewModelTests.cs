using GrpCurl.Net.Studio.TestSupport;
using GrpCurl.Net.Studio.ViewModels.Documents;
using GrpCurl.Net.Studio.ViewModels.Models.Connections;
using GrpCurl.Net.Studio.ViewModels.Models.Descriptors;

namespace GrpCurl.Net.Studio.Tests.Unit;

public sealed class DescribeDocumentViewModelTests
{
    private static SavedConnection Conn() => new() { Name = "c", Address = "h:1" };

    // The fake returns a message named after whatever symbol is requested, so navigation is observable.
    private static FakeDescriptorService DescriberByName()
        => new()
        {
            OnDescribe = (_, symbol, _) => Task.FromResult(
                DescribeResult.Success(new MessageDescription(symbol, ShortName(symbol), "f.proto", [], [], $"{{\"of\":\"{symbol}\"}}")))
        };

    private static string ShortName(string s)
    {
        var i = s.LastIndexOfAny(['.', '/']);
        return i >= 0 ? s[(i + 1)..] : s;
    }

    private static DescribeDocumentViewModel Create(
        out FakeDescriptorService descriptors, out FakeClipboardService clipboard, out FakeDocumentHost host, string symbol = "pkg.Alpha")
    {
        descriptors = DescriberByName();
        clipboard = new FakeClipboardService();
        host = new FakeDocumentHost();
        return new DescribeDocumentViewModel(Conn(), symbol, descriptors, new ImmediateUiDispatcher(), clipboard, host);
    }

    [Fact]
    public void Loads_the_symbol_on_creation()
    {
        var doc = Create(out _, out _, out _, "pkg.Alpha");

        doc.IsLoaded.ShouldBeTrue();
        doc.CurrentSymbol.ShouldBe("pkg.Alpha");
        doc.Title.ShouldBe("Alpha");
        doc.Symbol.ShouldBeOfType<MessageDescription>();
        doc.HasTemplate.ShouldBeTrue();
    }

    [Fact]
    public void Navigate_loads_the_target_in_tab_and_records_history()
    {
        var doc = Create(out _, out _, out _, "pkg.Alpha");

        doc.BackCommand.CanExecute(null).ShouldBeFalse();
        doc.NavigateCommand.Execute(new TypeRef("pkg.Beta", Resolvable: true));

        doc.CurrentSymbol.ShouldBe("pkg.Beta");
        doc.Title.ShouldBe("Beta");
        doc.BackCommand.CanExecute(null).ShouldBeTrue();
        doc.ForwardCommand.CanExecute(null).ShouldBeFalse();
    }

    [Fact]
    public void Back_and_forward_walk_the_history()
    {
        var doc = Create(out _, out _, out _, "pkg.Alpha");
        doc.NavigateCommand.Execute(new TypeRef("pkg.Beta", true));

        doc.BackCommand.Execute(null);
        doc.CurrentSymbol.ShouldBe("pkg.Alpha");
        doc.ForwardCommand.CanExecute(null).ShouldBeTrue();

        doc.ForwardCommand.Execute(null);
        doc.CurrentSymbol.ShouldBe("pkg.Beta");
    }

    [Fact]
    public void Navigating_after_going_back_clears_the_forward_stack()
    {
        var doc = Create(out _, out _, out _, "pkg.Alpha");
        doc.NavigateCommand.Execute(new TypeRef("pkg.Beta", true));
        doc.BackCommand.Execute(null);

        doc.NavigateCommand.Execute(new TypeRef("pkg.Gamma", true));

        doc.CurrentSymbol.ShouldBe("pkg.Gamma");
        doc.ForwardCommand.CanExecute(null).ShouldBeFalse();
    }

    [Fact]
    public void Navigate_ignores_unresolvable_references()
    {
        var doc = Create(out _, out _, out _, "pkg.Alpha");

        doc.NavigateCommand.Execute(new TypeRef("string", Resolvable: false));

        doc.CurrentSymbol.ShouldBe("pkg.Alpha");
        doc.BackCommand.CanExecute(null).ShouldBeFalse();
    }

    [Fact]
    public void Open_in_new_tab_routes_to_the_document_host()
    {
        var doc = Create(out _, out _, out var host, "pkg.Alpha");

        doc.OpenInNewTabCommand.Execute(new TypeRef("pkg.Beta", true));

        doc.CurrentSymbol.ShouldBe("pkg.Alpha"); // current tab unchanged
        host.Last!.Value.Symbol.ShouldBe("pkg.Beta");
        host.Last.Value.NewTab.ShouldBeTrue();
    }

    [Fact]
    public async Task Copy_template_writes_the_template_json()
    {
        var doc = Create(out _, out var clipboard, out _, "pkg.Alpha");

        await doc.CopyTemplateJsonCommand.ExecuteAsync(null);

        clipboard.Text.ShouldBe("{\"of\":\"pkg.Alpha\"}");
    }

    [Fact]
    public void Failure_sets_the_error_state()
    {
        var descriptors = new FakeDescriptorService
        {
            OnDescribe = (_, _, _) => Task.FromResult(
                DescribeResult.Failure(new DescriptorLoadError("nope", null, ReflectionUnavailable: false)))
        };

        var doc = new DescribeDocumentViewModel(
            Conn(), "pkg.Missing", descriptors, new ImmediateUiDispatcher(), new FakeClipboardService(), new FakeDocumentHost());

        doc.HasError.ShouldBeTrue();
        doc.ErrorMessage.ShouldBe("nope");
        doc.HasTemplate.ShouldBeFalse();
    }
}
