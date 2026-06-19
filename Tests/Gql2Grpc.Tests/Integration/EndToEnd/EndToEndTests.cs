using Gql2Grpc.Commands;
using Gql2Grpc.Tests.Fixtures;
using System.Text.Json.Nodes;

namespace Gql2Grpc.Tests.Integration.EndToEnd;

[Collection("GrpcServer")]
public sealed class EndToEndTests(GrpcTestFixture fixture)
{
    private string Address => fixture.Address;

    private static string MappingPath => Path.Combine(AppContext.BaseDirectory, "TestData", "mappings", "testservice.yaml");

    [Fact]
    public async Task Unary_empty_call_returns_graphql_envelope()
    {
        // Arrange

        // Act
        var (stdout, exitCode) = await RunAsync(
            Address,
            "--plaintext", "--mapping", MappingPath,
            "query { emptyCall }");

        // Assert
        exitCode.ShouldBe(0);

        var envelope = JsonNode.Parse(stdout)!.AsObject();
        envelope["data"]!.AsObject().ContainsKey("emptyCall").ShouldBeTrue();
        envelope.ContainsKey("errors").ShouldBeFalse();
    }

    [Fact]
    public async Task Unary_call_with_nested_request_payload()
    {
        // Arrange

        // Act
        var (stdout, exitCode) = await RunAsync(
            Address,
            "--plaintext", "--mapping", MappingPath,
            "query { unaryCall(input: { responseSize: 10 }) { payload { body } } }");

        // Assert
        exitCode.ShouldBe(0);

        var envelope = JsonNode.Parse(stdout)!.AsObject();
        envelope["data"]!.AsObject()["unaryCall"]!.AsObject()
            .ContainsKey("payload").ShouldBeTrue();
    }

    [Fact]
    public async Task Mutation_routed_to_unary_grpc()
    {
        // Arrange

        // Act
        var (stdout, exitCode) = await RunAsync(
            Address,
            "--plaintext", "--mapping", MappingPath,
            "mutation { createPayload(input: { responseSize: 5 }) { payload { body } } }");

        // Assert
        exitCode.ShouldBe(0);
        JsonNode.Parse(stdout)!.AsObject()["data"]!.AsObject()
            .ContainsKey("createPayload").ShouldBeTrue();
    }

    [Fact]
    public async Task Header_pass_through_triggers_fail_early()
    {
        // Arrange

        // Act
        var (stdout, exitCode) = await RunAsync(
            Address,
            "--plaintext", "--mapping", MappingPath,
            "-H", "fail-early: 3",
            "query { emptyCall }");

        // Assert
        exitCode.ShouldNotBe(0);

        var envelope = JsonNode.Parse(stdout)!.AsObject();
        envelope["errors"]!.AsArray().Count.ShouldBeGreaterThanOrEqualTo(1);
        var ext = envelope["errors"]!.AsArray()[0]!.AsObject()["extensions"]!.AsObject();
        ext["grpcStatusCode"]!.GetValue<int>().ShouldBe(3); // InvalidArgument
        ext["grpcStatus"]!.GetValue<string>().ShouldBe("InvalidArgument");
    }

    [Fact]
    public async Task Subscription_emits_ndjson_lines()
    {
        // Arrange

        // Act
        var (stdout, exitCode) = await RunAsync(
            Address,
            "--plaintext", "--mapping", MappingPath,
            "subscription { streamingOutput(input: { responseParameters: [{ size: 1 }, { size: 1 }] }) { payload { body } } }");

        // Assert
        exitCode.ShouldBe(0);

        var lines = stdout
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(l => l.StartsWith('{'))
            .ToArray();

        lines.Length.ShouldBeGreaterThanOrEqualTo(2);

        foreach (var line in lines)
        {
            var node = JsonNode.Parse(line)!.AsObject();
            node["data"]!.AsObject().ContainsKey("streamingOutput").ShouldBeTrue();
        }
    }

    [Fact]
    public async Task Introspection_schema_query_is_answered_locally()
    {
        // Arrange

        // Act
        var (stdout, exitCode) = await RunAsync(
            Address,
            "--plaintext", "--mapping", MappingPath,
            "query { __schema { queryType { name } } }");

        // Assert
        exitCode.ShouldBe(0);

        var envelope = JsonNode.Parse(stdout)!.AsObject();
        envelope["data"]!.AsObject()["__schema"]!.AsObject()["queryType"]!.AsObject()
            ["name"]!.GetValue<string>().ShouldBe("Query");
    }

    [Fact]
    public async Task Top_level_failure_emits_envelope_on_stdout_with_extensions()
    {
        // Arrange
        // Missing the GraphQL document is a top-level usage error caught above the executor.
        // It must still produce a parseable GraphQL envelope on stdout.

        // Act
        var (stdout, exitCode) = await RunAsync(Address, "--plaintext");

        // Assert
        exitCode.ShouldBe(2);

        var envelope = JsonNode.Parse(stdout)!.AsObject();

        envelope["data"].ShouldBeNull();
        envelope.ContainsKey("errors").ShouldBeTrue();

        var first = envelope["errors"]!.AsArray()[0]!.AsObject();

        first["message"]!.GetValue<string>().ShouldContain("No GraphQL document supplied");
        first["extensions"]!.AsObject()["code"]!.GetValue<string>().ShouldBe("USAGE");
    }

    [Fact]
    public async Task Reflection_based_discovery_works_without_mapping_file()
    {
        // Arrange

        // Act
        var (stdout, exitCode) = await RunAsync(
            Address,
            "--plaintext",
            "--default-service", "testing.TestService",
            "query { EmptyCall }");

        // Assert
        exitCode.ShouldBe(0);
        var envelope = JsonNode.Parse(stdout)!.AsObject();
        envelope["data"]!.AsObject().ContainsKey("EmptyCall").ShouldBeTrue();
    }

    [Fact]
    public async Task Parse_error_unknown_option_emits_usage_envelope_and_exits_2()
    {
        // Arrange

        // Act
        var (stdout, exitCode) = await RunAsync("--no-such-option");

        // Assert
        exitCode.ShouldBe(2);

        var envelope = JsonNode.Parse(stdout)!.AsObject();
        var error = envelope["errors"]!.AsArray()[0]!.AsObject();

        error["extensions"]!["code"]!.GetValue<string>().ShouldBe("USAGE");
    }

    [Fact]
    public async Task Parse_error_missing_address_emits_single_usage_envelope_and_exits_2()
    {
        // Arrange

        // Act — no positional address and no query: pure parse error.
        var (stdout, exitCode) = await RunAsync("--plaintext");

        // Assert
        exitCode.ShouldBe(2);

        // Exactly one envelope with one error (the default parse-error action used to
        // double-print missing-argument messages).
        var envelope = JsonNode.Parse(stdout)!.AsObject();
        var errors = envelope["errors"]!.AsArray();

        errors.Count.ShouldBe(1);
        errors[0]!["extensions"]!["code"]!.GetValue<string>().ShouldBe("USAGE");
    }

    private static async Task<(string Stdout, int ExitCode)> RunAsync(params string[] args)
    {
        var originalOut = Console.Out;
        var captured = new StringWriter();
        Console.SetOut(captured);

        try
        {
            var exitCode = await QueryCommandHandler.InvokeAsync(args);
            return (captured.ToString(), exitCode);
        }
        finally
        {
            Console.SetOut(originalOut);
        }
    }
}
