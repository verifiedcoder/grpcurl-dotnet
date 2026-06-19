using GrpCurl.Net.Studio.TestSupport;
using GrpCurl.Net.Studio.ViewModels.Documents;
using GrpCurl.Net.Studio.ViewModels.Models.Connections;
using GrpCurl.Net.Studio.ViewModels.Models.GraphQl;

namespace GrpCurl.Net.Studio.Tests.Unit;

public sealed class GraphQlDocumentViewModelTests
{
    private static SavedConnection Conn() => new() { Name = "c", Address = "h:1" };

    private static GraphQlDocumentViewModel Create(out FakeGraphQlService graphql, out FakeClipboardService clipboard)
    {
        graphql = new FakeGraphQlService();
        clipboard = new FakeClipboardService();
        return new GraphQlDocumentViewModel(Conn(), graphql, new ImmediateUiDispatcher(), clipboard)
        {
            ParseDebounce = TimeSpan.Zero
        };
    }

    private static GraphQlParseResult OneQuery(string name = "Q")
        => new([new GraphQlOperationInfo(name, GraphQlOperationKind.Query)], []);

    [Fact]
    public void A_single_operation_auto_selects_and_enables_execute()
    {
        var vm = Create(out _, out _);

        vm.ApplyParse(OneQuery());

        _ = vm.SelectedOperation.ShouldNotBeNull();
        vm.SelectedOperation!.Name.ShouldBe("Q");
        vm.ExecuteCommand.CanExecute(null).ShouldBeTrue();
    }

    [Fact]
    public void A_syntax_error_blocks_execute()
    {
        var vm = Create(out _, out _);

        vm.ApplyParse(new GraphQlParseResult([], [new GraphQlProblem("unexpected '}'", GraphQlProblemKind.Syntax)]));

        vm.HasSyntaxError.ShouldBeTrue();
        vm.HasProblems.ShouldBeTrue();
        vm.ExecuteCommand.CanExecute(null).ShouldBeFalse();
    }

    [Fact]
    public void Multiple_operations_require_an_explicit_selection()
    {
        var vm = Create(out _, out _);

        vm.ApplyParse(new GraphQlParseResult(
            [new GraphQlOperationInfo("A", GraphQlOperationKind.Query), new GraphQlOperationInfo("B", GraphQlOperationKind.Mutation)],
            []));

        vm.SelectedOperation.ShouldBeNull();
        vm.ExecuteCommand.CanExecute(null).ShouldBeFalse();

        vm.SelectedOperation = vm.Operations[1];

        vm.ExecuteCommand.CanExecute(null).ShouldBeTrue();
    }

    [Fact]
    public async Task Execute_sends_the_current_state_and_renders_the_envelope()
    {
        var vm = Create(out var graphql, out _);
        graphql.ParseResult = OneQuery();
        graphql.ExecuteResult = new(Ok: true, EnvelopeJson: "{\n  \"data\": { \"x\": 1 }\n}", ConfigurationErrors: []);

        vm.Document = "query Q { x }";
        vm.ApplyParse(graphql.ParseResult);
        vm.VariablesJson = "{\"v\":1}";
        vm.DefaultService = "pkg.Service";
        vm.EmitDefaults = true;

        await vm.ExecuteCommand.ExecuteAsync(null);

        graphql.ExecuteCount.ShouldBe(1);
        graphql.LastRequest!.Document.ShouldBe("query Q { x }");
        graphql.LastRequest.OperationName.ShouldBe("Q");
        graphql.LastRequest.VariablesJson.ShouldBe("{\"v\":1}");
        graphql.LastRequest.DefaultService.ShouldBe("pkg.Service");
        graphql.LastRequest.EmitDefaults.ShouldBeTrue();

        vm.ResponseJson.ShouldBe("{\n  \"data\": { \"x\": 1 }\n}");
        vm.State.ShouldBe(RunState.Completed);
        vm.HasResponse.ShouldBeTrue();
    }

    [Fact]
    public async Task A_configuration_error_surfaces_in_problems_and_makes_no_response()
    {
        var vm = Create(out var graphql, out _);
        graphql.ParseResult = OneQuery();
        graphql.ExecuteResult = new(Ok: false, EnvelopeJson: null,
            [new GraphQlProblem("variable $big is not a valid Int", GraphQlProblemKind.Variables)]);

        vm.Document = "query Q($big: Int) { x }";
        vm.ApplyParse(graphql.ParseResult);

        await vm.ExecuteCommand.ExecuteAsync(null);

        vm.ResponseJson.ShouldBeNull();
        vm.State.ShouldBe(RunState.Failed);
        vm.Problems.ShouldContain(p => p.Kind == GraphQlProblemKind.Variables);
    }

    [Fact]
    public async Task Cancellation_surfaces_as_a_cancelled_state()
    {
        var vm = Create(out var graphql, out _);
        graphql.ParseResult = OneQuery();
        graphql.OnExecute = (_, _) => throw new OperationCanceledException();

        vm.Document = "query Q { x }";
        vm.ApplyParse(graphql.ParseResult);

        await vm.ExecuteCommand.ExecuteAsync(null);

        vm.State.ShouldBe(RunState.Cancelled);
    }

    [Fact]
    public async Task Copy_response_puts_the_envelope_on_the_clipboard()
    {
        var vm = Create(out var graphql, out var clipboard);
        graphql.ParseResult = OneQuery();
        graphql.ExecuteResult = new(Ok: true, EnvelopeJson: "{ \"data\": {} }", ConfigurationErrors: []);

        vm.ApplyParse(graphql.ParseResult);
        await vm.ExecuteCommand.ExecuteAsync(null);
        await vm.CopyResponseCommand.ExecuteAsync(null);

        clipboard.Text.ShouldBe("{ \"data\": {} }");
    }

    [Fact]
    public void Re_parsing_keeps_the_prior_selection_when_the_operation_still_exists()
    {
        var vm = Create(out _, out _);
        vm.ApplyParse(new GraphQlParseResult(
            [new GraphQlOperationInfo("A", GraphQlOperationKind.Query), new GraphQlOperationInfo("B", GraphQlOperationKind.Query)],
            []));
        vm.SelectedOperation = vm.Operations[1]; // B

        vm.ApplyParse(new GraphQlParseResult(
            [new GraphQlOperationInfo("A", GraphQlOperationKind.Query), new GraphQlOperationInfo("B", GraphQlOperationKind.Query)],
            []));

        vm.SelectedOperation!.Name.ShouldBe("B");
    }
}
