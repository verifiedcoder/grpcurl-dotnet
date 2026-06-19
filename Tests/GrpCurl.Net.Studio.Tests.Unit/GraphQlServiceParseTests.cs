using GrpCurl.Net.Studio.Services;
using GrpCurl.Net.Studio.ViewModels.Models.Connections;
using GrpCurl.Net.Studio.ViewModels.Models.GraphQl;

namespace GrpCurl.Net.Studio.Tests.Unit;

/// <summary>
///     Exercises the real bridge parser through <see cref="GraphQlService" /> (no network): operation
///     enumeration for the picker (GQL-012) and syntax problems with editor positions (GQL-010/011).
/// </summary>
public sealed class GraphQlServiceParseTests
{
    private static GraphQlService Service() => new();

    [Fact]
    public void Parse_enumerates_named_operations_with_their_kinds()
    {
        var result = Service().Parse("query GetA { a }\nmutation SetB { b }\nsubscription OnC { c }");

        result.Ok.ShouldBeTrue();
        result.Operations.Count.ShouldBe(3);
        (result.Operations[0].Name, result.Operations[0].Kind).ShouldBe(("GetA", GraphQlOperationKind.Query));
        (result.Operations[1].Name, result.Operations[1].Kind).ShouldBe(("SetB", GraphQlOperationKind.Mutation));
        (result.Operations[2].Name, result.Operations[2].Kind).ShouldBe(("OnC", GraphQlOperationKind.Subscription));
    }

    [Fact]
    public void Parse_reports_each_operations_declared_variables_with_required_flags()
    {
        var result = Service().Parse("query Sizes($big: Int!, $name: String = \"x\", $tags: [String!]) { a }");

        var variables = result.Operations.ShouldHaveSingleItem().Variables;
        variables.Count.ShouldBe(3);

        variables[0].ShouldBe(new GraphQlVariableInfo("big", "Int!", Required: true));
        // A non-null type with a default is not "required" (the default supplies it).
        variables[1].ShouldBe(new GraphQlVariableInfo("name", "String", Required: false));
        variables[2].ShouldBe(new GraphQlVariableInfo("tags", "[String!]", Required: false));
    }

    [Fact]
    public void Parse_reports_a_syntax_problem_with_a_position_for_a_malformed_document()
    {
        var result = Service().Parse("query Broken { a ");

        result.Ok.ShouldBeFalse();
        var problem = result.Problems.ShouldHaveSingleItem();
        problem.Kind.ShouldBe(GraphQlProblemKind.Syntax);
        _ = problem.Line.ShouldNotBeNull();
        problem.Line!.Value.ShouldBeGreaterThan(0);
        problem.Column!.Value.ShouldBeGreaterThan(0);
    }

    [Fact]
    public void Parse_reports_a_syntax_problem_for_an_empty_document()
    {
        var result = Service().Parse("   ");

        result.Ok.ShouldBeFalse();
        result.Problems.ShouldContain(p => p.Kind == GraphQlProblemKind.Syntax);
    }

    [Fact]
    public void Parse_counts_root_fields_for_the_subscription_pre_flight()
    {
        var result = Service().Parse("subscription Two { a b }");

        result.Operations.ShouldHaveSingleItem().RootFieldCount.ShouldBe(2);
    }

    [Fact]
    public async Task StreamAsync_yields_a_single_error_envelope_for_a_malformed_document()
    {
        var request = new GraphQlExecutionRequest(
            new SavedConnection { Name = "c", Address = "h:1" },
            Document: "subscription { ", OperationName: null, VariablesJson: null, DefaultService: null, MappingPath: null,
            Headers: [], Deadline: null, EmitDefaults: false, AllowUnknownFields: true, StrictSelection: false,
            Introspection: true, Raw: false);

        var lines = new List<string>();
        await foreach (var line in Service().StreamAsync(request, TestContext.Current.CancellationToken))
        {
            lines.Add(line);
        }

        lines.ShouldHaveSingleItem().ShouldContain("errors"); // a setup failure surfaces as one error envelope, no RPC
    }
}
