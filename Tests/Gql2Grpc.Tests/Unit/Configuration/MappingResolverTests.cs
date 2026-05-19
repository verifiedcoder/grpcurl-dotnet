using Gql2Grpc.Configuration;
using Gql2Grpc.GraphQL;

namespace Gql2Grpc.Tests.Unit.Configuration;

public sealed class MappingResolverTests
{
    [Fact]
    public void Explicit_entry_wins_over_convention()
    {
        // Arrange
        var config = new MappingConfig
        {
            Defaults = new MappingDefaults { Service = "default.Svc" },
            Operations =
            [
                new MappingEntry
                {
                    GraphqlField = "foo",
                    OperationType = GraphQLOperationType.Query,
                    Service = "explicit.Svc",
                    Method = "DoFoo"
                }
            ]
        };

        var resolver = new MappingResolver(config, cliDefaultService: null);

        // Act
        var entry = resolver.Resolve("foo", GraphQLOperationType.Query);

        // Assert
        entry.Service.ShouldBe("explicit.Svc");
        entry.Method.ShouldBe("DoFoo");
    }

    [Fact]
    public void Cli_default_service_overrides_config_default_for_convention()
    {
        // Arrange
        var config = new MappingConfig
        {
            Defaults = new MappingDefaults { Service = "default.Svc" }
        };

        var resolver = new MappingResolver(config, cliDefaultService: "cli.Svc");

        // Act
        var entry = resolver.Resolve("foo", GraphQLOperationType.Query);

        // Assert
        entry.Service.ShouldBe("cli.Svc");
        entry.Method.ShouldBe("Foo");
    }

    [Fact]
    public void Convention_fallback_pascal_cases_method_name()
    {
        // Arrange
        var config = new MappingConfig
        {
            Defaults = new MappingDefaults { Service = "default.Svc" }
        };

        // Act
        var resolver = new MappingResolver(config, cliDefaultService: null);

        // Assert
        resolver.Resolve("activeResponses", GraphQLOperationType.Query).Method
            .ShouldBe("ActiveResponses");
    }

    [Fact]
    public void Convention_fallback_selects_server_streaming_for_subscription()
    {
        // Arrange
        var config = new MappingConfig
        {
            Defaults = new MappingDefaults { Service = "default.Svc" }
        };

        // Act
        var resolver = new MappingResolver(config, cliDefaultService: null);

        // Assert
        resolver.Resolve("events", GraphQLOperationType.Subscription).Kind
            .ShouldBe(MethodKind.ServerStreaming);
    }

    [Fact]
    public void Missing_service_for_entry_falls_back_to_defaults()
    {
        // Arrange
        var config = new MappingConfig
        {
            Defaults = new MappingDefaults { Service = "default.Svc" },
            Operations =
            [
                new MappingEntry
                {
                    GraphqlField = "foo",
                    OperationType = GraphQLOperationType.Query,
                    Method = "DoFoo"
                }
            ]
        };

        // Act
        var resolver = new MappingResolver(config, cliDefaultService: null);

        // Assert
        resolver.Resolve("foo", GraphQLOperationType.Query).Service.ShouldBe("default.Svc");
    }
}
