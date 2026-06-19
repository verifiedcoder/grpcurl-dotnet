using GrpCurl.Net.Studio.Services;
using GrpCurl.Net.Studio.ViewModels.Models.Connections;
using GrpCurl.Net.Studio.ViewModels.Models.GraphQl;

namespace GrpCurl.Net.Studio.Tests.Unit;

/// <summary>
///     GQL-050/051/047: the translation inspector, exercised offline against <c>test.protoset</c> (no
///     server) — request JSON, per-argument rule annotations, the FieldMask, and the dropped-argument guard.
/// </summary>
public sealed class GraphQlServiceTranslationTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static SavedConnection ProtosetConnection()
    {
        var path = Path.Combine(
            Path.GetDirectoryName(typeof(GraphQlServiceTranslationTests).Assembly.Location)!,
            "TestProtosets", "test.protoset");

        return new SavedConnection
        {
            Name = "c",
            Address = "localhost:1", // never dialled — translation reads descriptors only
            DescriptorSource = new DescriptorSourceConfig { Mode = DescriptorMode.Protoset, ProtosetPaths = [path] }
        };
    }

    private static GraphQlExecutionRequest Request(string document, string mapping) => new(
        ProtosetConnection(), document, OperationName: null, VariablesJson: null, DefaultService: null, MappingPath: null,
        Headers: [], Deadline: null, EmitDefaults: false, AllowUnknownFields: true, StrictSelection: false,
        Introspection: true, Raw: false, Verbosity: GraphQlVerbosity.Off, MappingText: mapping);

    private static async Task<GraphQlFieldTranslation> TranslateOne(string document, string mapping)
        => (await new GraphQlService().TranslateAsync(Request(document, mapping), Ct)).Fields.ShouldHaveSingleItem();

    [Fact]
    public async Task A_convention_argument_is_annotated_as_snake_case_and_lands_in_the_request()
    {
        var field = await TranslateOne(
            "query Q { unaryCall(responseSize: 5) { payload { body } } }",
            "version: 1\noperations:\n  - graphqlField: unaryCall\n    service: testing.TestService\n    method: UnaryCall");

        field.Error.ShouldBeNull();
        field.HasRequestJson.ShouldBeTrue();
        field.RequestJson!.ShouldContain("response_size");
        field.Annotations.ShouldContain(a => a.Argument == "responseSize" && a.Rule == "snake_case" && a.Target == "response_size");
        field.HasDroppedArguments.ShouldBeFalse();
    }

    [Fact]
    public async Task A_renamed_argument_is_annotated_as_rename()
    {
        var field = await TranslateOne(
            "query Q { unaryCall(size: 7) { payload { body } } }",
            "version: 1\noperations:\n  - graphqlField: unaryCall\n    service: testing.TestService\n    method: UnaryCall\n    arguments:\n      size: response_size");

        field.Annotations.ShouldContain(a => a.Argument == "size" && a.Rule == "rename" && a.Target == "response_size");
        field.RequestJson!.ShouldContain("response_size");
    }

    [Fact]
    public async Task An_argument_matching_no_field_is_reported_as_dropped()
    {
        var field = await TranslateOne(
            "query Q { unaryCall(noSuchArg: 1) { payload { body } } }",
            "version: 1\noperations:\n  - graphqlField: unaryCall\n    service: testing.TestService\n    method: UnaryCall");

        field.DroppedArguments.ShouldContain("noSuchArg");
    }

    [Fact]
    public async Task A_selection_field_mask_is_synthesised()
    {
        var field = await TranslateOne(
            "query Q { unaryCall { payload { body } } }",
            "version: 1\noperations:\n  - graphqlField: unaryCall\n    service: testing.TestService\n    method: UnaryCall\n    arguments:\n      $selection: { fieldMask: response_size }");

        field.HasFieldMask.ShouldBeTrue();
    }
}
