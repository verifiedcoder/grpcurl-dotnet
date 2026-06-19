using GrpCurl.Net.Studio.Services;
using GrpCurl.Net.Studio.ViewModels.Models.Connections;
using GrpCurl.Net.Studio.ViewModels.Models.GraphQl;

namespace GrpCurl.Net.Studio.Tests.Unit;

/// <summary>GQL-040..043: root-field resolution is computed purely (no RPC) for the live preview.</summary>
public sealed class GraphQlServiceResolutionTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static GraphQlExecutionRequest Request(string document, string? defaultService) => new(
        new SavedConnection { Name = "c", Address = "h:1" },
        Document: document, OperationName: null, VariablesJson: null, DefaultService: defaultService, MappingPath: null,
        Headers: [], Deadline: null, EmitDefaults: false, AllowUnknownFields: true, StrictSelection: false,
        Introspection: true, Raw: false);

    [Fact]
    public async Task A_default_service_resolves_root_fields_by_convention()
    {
        var result = await new GraphQlService().ResolveAsync(
            Request("query Q { unaryCall emptyCall }", "testing.TestService"), Ct);

        result.Fields.Count.ShouldBe(2);

        var unary = result.Fields[0];
        unary.FieldName.ShouldBe("unaryCall");
        unary.Resolved.ShouldBeTrue();
        unary.Source.ShouldBe(GraphQlResolutionSource.Convention);
        unary.Service.ShouldBe("testing.TestService");
        unary.Method.ShouldBe("UnaryCall"); // PascalCase convention default
        unary.Kind.ShouldBe("unary");
        unary.HasDerivation.ShouldBeTrue();
    }

    [Fact]
    public async Task An_unmappable_field_is_flagged_unresolved_with_a_remedy()
    {
        var result = await new GraphQlService().ResolveAsync(Request("query Q { foo }", defaultService: null), Ct);

        var field = result.Fields.ShouldHaveSingleItem();
        field.Resolved.ShouldBeFalse();
        field.IsUnresolved.ShouldBeTrue();
        _ = field.Error.ShouldNotBeNull();
        field.Error!.ShouldContain("foo");
    }

    [Fact]
    public async Task A_malformed_document_yields_no_resolutions()
        => (await new GraphQlService().ResolveAsync(Request("query {", null), Ct)).Fields.ShouldBeEmpty();

    [Fact]
    public void ValidateMapping_accepts_a_clean_mapping()
        => new GraphQlService().ValidateMapping("version: 1\noperations:\n  - graphqlField: foo\n    method: GetFoo").ShouldBeEmpty();

    [Fact]
    public void ValidateMapping_reports_an_invalid_mapping()
    {
        var problems = new GraphQlService().ValidateMapping("operations:\n  - graphqlField: foo"); // missing 'method'

        problems.ShouldHaveSingleItem().Kind.ShouldBe(GraphQlProblemKind.Configuration);
    }

    [Fact]
    public async Task An_inline_mapping_resolves_a_field_explicitly()
    {
        var request = Request("query Q { foo }", defaultService: null) with
        {
            MappingText = "version: 1\ndefaults:\n  service: pkg.Service\noperations:\n  - graphqlField: foo\n    method: GetFoo"
        };

        var field = (await new GraphQlService().ResolveAsync(request, Ct)).Fields.ShouldHaveSingleItem();
        field.Resolved.ShouldBeTrue();
        field.Source.ShouldBe(GraphQlResolutionSource.ExplicitEntry);
        field.Method.ShouldBe("GetFoo");
        field.Service.ShouldBe("pkg.Service");
    }
}
