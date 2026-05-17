using Gql2Grpc.Configuration;
using Gql2Grpc.GraphQL;

namespace Gql2Grpc.Tests.Unit.Configuration;

public sealed class MappingResolverTests
{
    [Fact]
    public void Explicit_entry_wins_over_convention()
    {
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
        var entry = resolver.Resolve("foo", GraphQLOperationType.Query);

        entry.Service.ShouldBe("explicit.Svc");
        entry.Method.ShouldBe("DoFoo");
    }

    [Fact]
    public void Cli_default_service_overrides_config_default_for_convention()
    {
        var config = new MappingConfig
        {
            Defaults = new MappingDefaults { Service = "default.Svc" }
        };

        var resolver = new MappingResolver(config, cliDefaultService: "cli.Svc");
        var entry = resolver.Resolve("foo", GraphQLOperationType.Query);

        entry.Service.ShouldBe("cli.Svc");
        entry.Method.ShouldBe("Foo");
    }

    [Fact]
    public void Convention_fallback_pascal_cases_method_name()
    {
        var config = new MappingConfig
        {
            Defaults = new MappingDefaults { Service = "default.Svc" }
        };
        var resolver = new MappingResolver(config, cliDefaultService: null);

        resolver.Resolve("activeResponses", GraphQLOperationType.Query).Method
            .ShouldBe("ActiveResponses");
    }

    [Fact]
    public void Convention_fallback_selects_server_streaming_for_subscription()
    {
        var config = new MappingConfig
        {
            Defaults = new MappingDefaults { Service = "default.Svc" }
        };
        var resolver = new MappingResolver(config, cliDefaultService: null);

        resolver.Resolve("events", GraphQLOperationType.Subscription).Kind
            .ShouldBe(MethodKind.ServerStreaming);
    }

    [Fact]
    public void Missing_service_for_entry_falls_back_to_defaults()
    {
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
        var resolver = new MappingResolver(config, cliDefaultService: null);

        resolver.Resolve("foo", GraphQLOperationType.Query).Service.ShouldBe("default.Svc");
    }
}
