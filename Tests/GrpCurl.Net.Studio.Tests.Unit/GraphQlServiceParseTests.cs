using GrpCurl.Net.Studio.Services;
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
        result.Operations[0].ShouldBe(new GraphQlOperationInfo("GetA", GraphQlOperationKind.Query));
        result.Operations[1].ShouldBe(new GraphQlOperationInfo("SetB", GraphQlOperationKind.Mutation));
        result.Operations[2].ShouldBe(new GraphQlOperationInfo("OnC", GraphQlOperationKind.Subscription));
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
}
