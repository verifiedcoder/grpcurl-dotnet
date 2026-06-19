using GrpCurl.Net.Studio.Services;
using GrpCurl.Net.Studio.ViewModels.Models.Connections;
using GrpCurl.Net.Studio.ViewModels.Models.GraphQl;

namespace GrpCurl.Net.Studio.Tests.Unit;

/// <summary>
///     GQL-015: descriptor-aware editor completions, exercised offline against <c>test.protoset</c> (no
///     server) — root field names from convention + explicit mapping, and per-field argument names.
/// </summary>
public sealed class GraphQlServiceCompletionTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static SavedConnection ProtosetConnection()
    {
        var path = Path.Combine(
            Path.GetDirectoryName(typeof(GraphQlServiceCompletionTests).Assembly.Location)!,
            "TestProtosets", "test.protoset");

        return new SavedConnection
        {
            Name = "c",
            Address = "localhost:1", // never dialled — completion reads descriptors only
            DescriptorSource = new DescriptorSourceConfig { Mode = DescriptorMode.Protoset, ProtosetPaths = [path] }
        };
    }

    private static GraphQlExecutionRequest Request(string document, string? defaultService, string? mapping = null) => new(
        ProtosetConnection(), document, OperationName: null, VariablesJson: null, DefaultService: defaultService, MappingPath: null,
        Headers: [], Deadline: null, EmitDefaults: false, AllowUnknownFields: true, StrictSelection: false,
        Introspection: true, Raw: false, Verbosity: GraphQlVerbosity.Off, MappingText: mapping);

    [Fact]
    public async Task Convention_root_fields_are_the_default_service_methods_camel_cased()
    {
        var completions = await new GraphQlService().GetCompletionsAsync(
            Request("query Q { }", "testing.TestService"), Ct);

        completions.RootFields.ShouldContain("unaryCall");
        completions.RootFields.ShouldContain("emptyCall");
        completions.RootFields.ShouldAllBe(f => f.Length > 0 && char.IsLower(f[0]));
    }

    [Fact]
    public async Task Arguments_for_a_root_field_are_its_request_message_fields_camel_cased()
    {
        var completions = await new GraphQlService().GetCompletionsAsync(
            Request("query Q { }", "testing.TestService"), Ct);

        var args = completions.ArgumentsFor("unaryCall");
        args.ShouldContain("responseSize"); // response_size
        args.ShouldContain("responseType"); // response_type
        args.ShouldContain("fillUsername"); // fill_username
    }

    [Fact]
    public async Task An_explicit_mapping_entry_contributes_a_root_field()
    {
        var completions = await new GraphQlService().GetCompletionsAsync(
            Request(
                "query Q { }",
                defaultService: null,
                mapping: "version: 1\noperations:\n  - graphqlField: foo\n    service: testing.TestService\n    method: UnaryCall"),
            Ct);

        completions.RootFields.ShouldContain("foo");
        completions.ArgumentsFor("foo").ShouldContain("responseSize");
    }

    [Fact]
    public async Task An_unknown_field_yields_no_arguments()
        => (await new GraphQlService().GetCompletionsAsync(Request("query Q { }", "testing.TestService"), Ct))
            .ArgumentsFor("noSuchField").ShouldBeEmpty();

    [Theory]
    [InlineData("response_size", "responseSize")]
    [InlineData("fill_username", "fillUsername")]
    [InlineData("UnaryCall", "unaryCall")]
    [InlineData("body", "body")]
    [InlineData("", "")]
    public void ToCamelCase_handles_snake_case_and_pascal_case(string input, string expected)
        => GraphQlService.ToCamelCase(input).ShouldBe(expected);
}
