using Gql2Grpc.Configuration;
using GrpCurl.Net.DescriptorSources;
using GrpCurl.Net.Studio.Services;

namespace GrpCurl.Net.Studio.Tests.Unit;

/// <summary>
///     GQL-046: descriptor-aware mapping validation, exercised offline against the vendored
///     <c>test.protoset</c> (no server) — service/method existence, kind, argument paths, and unwrap.
/// </summary>
public sealed class GraphQlServiceMappingValidationTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task<IDescriptorSource> TestServiceSource()
    {
        var path = Path.Combine(
            Path.GetDirectoryName(typeof(GraphQlServiceMappingValidationTests).Assembly.Location)!,
            "TestProtosets", "test.protoset");

        return await ProtosetSource.LoadFromFilesAsync([path], Ct);
    }

    private static async Task<IReadOnlyList<string>> Validate(string mappingYaml)
    {
        var source = await TestServiceSource();
        var config = MappingConfigLoader.FromText(mappingYaml);
        return (await GraphQlService.ValidateEntriesAsync(config, source, defaultService: null, Ct)).Select(p => p.Message).ToList();
    }

    [Fact]
    public async Task A_correct_entry_has_no_problems()
        => (await Validate("version: 1\noperations:\n  - graphqlField: u\n    service: testing.TestService\n    method: UnaryCall")).ShouldBeEmpty();

    [Fact]
    public async Task An_unknown_method_is_flagged_with_a_suggestion()
    {
        var problems = await Validate("version: 1\noperations:\n  - graphqlField: u\n    service: testing.TestService\n    method: UnaryCll");

        problems.ShouldHaveSingleItem().ShouldContain("did you mean 'UnaryCall'");
    }

    [Fact]
    public async Task An_unknown_service_is_flagged()
    {
        var problems = await Validate("version: 1\noperations:\n  - graphqlField: u\n    service: testing.NoSuchService\n    method: UnaryCall");

        problems.ShouldHaveSingleItem().ShouldContain("was not found");
    }

    [Fact]
    public async Task A_kind_mismatch_is_flagged()
    {
        // StreamingOutputCall is server-streaming, but the entry leaves kind at its unary default.
        var problems = await Validate("version: 1\noperations:\n  - graphqlField: s\n    service: testing.TestService\n    method: StreamingOutputCall");

        problems.ShouldContain(p => p.Contains("kind") && p.Contains("serverStreaming"));
    }

    [Fact]
    public async Task An_argument_path_that_misses_a_field_is_flagged()
    {
        var problems = await Validate(
            "version: 1\noperations:\n  - graphqlField: u\n    service: testing.TestService\n    method: UnaryCall\n    arguments:\n      foo: { path: no_such_field }");

        problems.ShouldContain(p => p.Contains("no_such_field") && p.Contains("not a field"));
    }

    [Fact]
    public async Task A_valid_nested_argument_path_is_accepted()
        => (await Validate(
            "version: 1\noperations:\n  - graphqlField: u\n    service: testing.TestService\n    method: UnaryCall\n    arguments:\n      foo: { path: payload.body }")).ShouldBeEmpty();

    [Fact]
    public async Task An_unknown_response_unwrap_is_flagged()
    {
        var problems = await Validate(
            "version: 1\noperations:\n  - graphqlField: u\n    service: testing.TestService\n    method: UnaryCall\n    response:\n      unwrap: no_such");

        problems.ShouldContain(p => p.Contains("response.unwrap") && p.Contains("no_such"));
    }

    [Fact]
    public void Closest_suggests_a_near_match_and_nothing_for_a_far_one()
    {
        GraphQlService.Closest(["UnaryCall", "EmptyCall"], "UnaryCll").ShouldBe("UnaryCall");
        GraphQlService.Closest(["UnaryCall"], "completely_different").ShouldBeNull();
    }
}
