using Gql2Grpc.Configuration;
using Gql2Grpc.GraphQL;
using GrpCurl.Net.Utilities;

namespace Gql2Grpc.Tests.Unit.Configuration;

public sealed class MappingConfigLoaderTests
{
    [Fact]
    public async Task Parses_yaml_into_mapping_config()
    {
        // Arrange
        const string yaml = @"
version: 1
defaults:
  service: test.Svc
operations:
  - graphqlField: foo
    operationType: query
    method: DoFoo
";
        var path = WriteTemp(yaml, ".yaml");

        // Act
        var config = await MappingConfigLoader.LoadAsync(path, TestContext.Current.CancellationToken);

        // Assert
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
        // Arrange
        const string json = """{ "operations": [{ "graphqlField": "x", "method": "GoX", "service": "s.Svc" }] }""";

        var path = WriteTemp(json, ".json");

        // Act
        var config = await MappingConfigLoader.LoadAsync(path, TestContext.Current.CancellationToken);

        // Assert
        config.Operations.Count.ShouldBe(1);
        config.Operations[0].Service.ShouldBe("s.Svc");
    }

    [Fact]
    public async Task Argument_rule_rename_is_recognised()
    {
        // Arrange
        const string yaml = @"
operations:
  - graphqlField: foo
    method: M
    service: s.Svc
    arguments:
      a: b
";
        var path = WriteTemp(yaml, ".yaml");

        // Act
        var config = await MappingConfigLoader.LoadAsync(path, TestContext.Current.CancellationToken);

        // Assert
        config.Operations[0].Arguments.ShouldContainKey("a");
        _ = config.Operations[0].Arguments["a"].ShouldBeOfType<ArgumentRule.Rename>();
    }

    [Fact]
    public async Task Argument_rule_path_and_literal_are_recognised()
    {
        // Arrange
        const string yaml = @"
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

        // Act
        var args = config.Operations[0].Arguments;

        // Assert
        args["a"].ShouldBeOfType<ArgumentRule.PathRule>().Path.ShouldBe("p.q");
        args["b"].ShouldBeOfType<ArgumentRule.Literal>().Value.ShouldBe("hello");
        _ = args["c"].ShouldBeOfType<ArgumentRule.SkipArgument>();
        config.Operations[0].SelectionFieldMaskPath.ShouldBe("read_mask");
    }

    [Fact]
    public async Task Duplicate_field_operation_pairs_rejected()
    {
        // Arrange
        const string yaml = @"
operations:
  - graphqlField: foo
    method: M1
    service: s.Svc
  - graphqlField: foo
    method: M2
    service: s.Svc
";

        // Act
        var path = WriteTemp(yaml, ".yaml");

        // Assert
        _ = await Should.ThrowAsync<InvalidDataException>(async () =>
            await MappingConfigLoader.LoadAsync(path, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Missing_path_returns_empty_config()
    {
        // Arrange

        // Act
        var config = await MappingConfigLoader.LoadAsync(null, TestContext.Current.CancellationToken);

        // Assert
        config.ShouldBeSameAs(MappingConfig.Empty);
    }

    [Fact]
    public async Task Oversized_mapping_file_is_rejected()
    {
        // Arrange
        var contents = new string('a', (int)InputFileGuard.MaxMappingConfigBytes + 1);
        var path = WriteTemp(contents, ".yaml");

        try
        {
            // Act
            var exception = await Should.ThrowAsync<InvalidDataException>(async () =>
                await MappingConfigLoader.LoadAsync(path, TestContext.Current.CancellationToken));

            // Assert
            exception.Message.ShouldContain("Mapping configuration file");
            exception.Message.ShouldContain("maximum allowed");
        }
        finally
        {
            File.Delete(path);
        }
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
