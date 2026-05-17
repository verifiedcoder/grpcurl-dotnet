using Gql2Grpc.Configuration;
using Gql2Grpc.GraphQL;

namespace Gql2Grpc.Tests.Unit.Configuration;

public sealed class MappingConfigLoaderTests
{
    [Fact]
    public async Task Parses_yaml_into_mapping_config()
    {
        var yaml = @"
version: 1
defaults:
  service: test.Svc
operations:
  - graphqlField: foo
    operationType: query
    method: DoFoo
";
        var path = WriteTemp(yaml, ".yaml");
        var config = await MappingConfigLoader.LoadAsync(path, TestContext.Current.CancellationToken);

        config.Version.ShouldBe(1);
        config.Defaults.Service.ShouldBe("test.Svc");
        config.Operations.Count.ShouldBe(1);
        config.Operations[0].GraphqlField.ShouldBe("foo");
        config.Operations[0].Method.ShouldBe("DoFoo");
        config.Operations[0].OperationType.ShouldBe(GraphQLOperationType.Query);
    }

    [Fact]
    public async Task Parses_json_into_mapping_config()
    {
        var json = """{ "operations": [{ "graphqlField": "x", "method": "GoX", "service": "s.Svc" }] }""";
        var path = WriteTemp(json, ".json");
        var config = await MappingConfigLoader.LoadAsync(path, TestContext.Current.CancellationToken);

        config.Operations.Count.ShouldBe(1);
        config.Operations[0].Service.ShouldBe("s.Svc");
    }

    [Fact]
    public async Task Argument_rule_rename_is_recognised()
    {
        var yaml = @"
operations:
  - graphqlField: foo
    method: M
    service: s.Svc
    arguments:
      a: b
";
        var path = WriteTemp(yaml, ".yaml");
        var config = await MappingConfigLoader.LoadAsync(path, TestContext.Current.CancellationToken);

        config.Operations[0].Arguments.ShouldContainKey("a");
        config.Operations[0].Arguments["a"].ShouldBeOfType<ArgumentRule.Rename>();
    }

    [Fact]
    public async Task Argument_rule_path_and_literal_are_recognised()
    {
        var yaml = @"
operations:
  - graphqlField: foo
    method: M
    service: s.Svc
    arguments:
      a: { path: p.q }
      b: { literal: hello }
      c: { skip: true }
      $selection: { fieldMask: read_mask }
";
        var path = WriteTemp(yaml, ".yaml");
        var config = await MappingConfigLoader.LoadAsync(path, TestContext.Current.CancellationToken);

        var args = config.Operations[0].Arguments;
        args["a"].ShouldBeOfType<ArgumentRule.PathRule>().Path.ShouldBe("p.q");
        args["b"].ShouldBeOfType<ArgumentRule.Literal>().Value.ShouldBe("hello");
        args["c"].ShouldBeOfType<ArgumentRule.SkipArgument>();
        config.Operations[0].SelectionFieldMaskPath.ShouldBe("read_mask");
    }

    [Fact]
    public async Task Duplicate_field_operation_pairs_rejected()
    {
        var yaml = @"
operations:
  - graphqlField: foo
    method: M1
    service: s.Svc
  - graphqlField: foo
    method: M2
    service: s.Svc
";
        var path = WriteTemp(yaml, ".yaml");
        await Should.ThrowAsync<InvalidDataException>(async () =>
            await MappingConfigLoader.LoadAsync(path, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Missing_path_returns_empty_config()
    {
        var config = await MappingConfigLoader.LoadAsync(null, TestContext.Current.CancellationToken);
        config.ShouldBeSameAs(MappingConfig.Empty);
    }

    private static string WriteTemp(string contents, string ext)
    {
        var path = Path.GetTempFileName();
        var renamed = path + ext;
        File.Move(path, renamed);
        File.WriteAllText(renamed, contents);
        return renamed;
    }
}
