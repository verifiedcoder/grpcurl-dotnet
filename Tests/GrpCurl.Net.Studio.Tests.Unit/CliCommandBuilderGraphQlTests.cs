using GrpCurl.Net.Studio.ViewModels.Models.Connections;
using GrpCurl.Net.Studio.ViewModels.Models.GraphQl;
using GrpCurl.Net.Studio.ViewModels.Services;

namespace GrpCurl.Net.Studio.Tests.Unit;

/// <summary>
///     GQL-028: the "Copy as CLI" command for a GraphQL tab produces a <c>gql2grpc</c> command that
///     round-trips through the real CLI parser, emits secrets as <c>${VAR}</c> placeholders, and maps
///     the tab's options/toggles to the matching flags.
/// </summary>
public sealed class CliCommandBuilderGraphQlTests
{
    private static GraphQlExecutionRequest Request(
        bool allowUnknownFields = true, bool introspection = true, string? variables = "{ \"a\": 1 }") => new(
        new SavedConnection { Name = "c", Address = "api.example.com:443", Transport = TransportMode.Tls, ServerName = "api" },
        Document: "query Q($a: Int) { x }",
        OperationName: "Q",
        VariablesJson: variables,
        DefaultService: "pkg.Service",
        MappingPath: null,
        Headers: [new HeaderEntry { Name = "authorization", Value = "Bearer super-secret" }],
        Deadline: "30s",
        EmitDefaults: true,
        AllowUnknownFields: allowUnknownFields,
        StrictSelection: true,
        Introspection: introspection,
        Raw: true);

    [Fact]
    public void The_generated_command_parses_through_the_real_gql2grpc_parser()
    {
        var args = CliCommandBuilder.BuildGraphQlArgs(Request(allowUnknownFields: false, introspection: false)).ToArray();

        var parse = Gql2Grpc.Commands.QueryCommandHandler.Create().Parse(args);

        parse.Errors.ShouldBeEmpty();
    }

    [Fact]
    public void Secrets_are_emitted_as_placeholders_never_literals()
    {
        var args = CliCommandBuilder.BuildGraphQlArgs(Request());

        args.ShouldContain("authorization: ${AUTHORIZATION}");
        args.ShouldNotContain(a => a.Contains("super-secret"));
    }

    [Fact]
    public void Options_and_toggles_map_to_the_matching_flags()
    {
        var args = CliCommandBuilder.BuildGraphQlArgs(Request()).ToList();

        args.ShouldContain("--default-service");
        args[args.IndexOf("--default-service") + 1].ShouldBe("pkg.Service");
        args.ShouldContain("--operation");
        args[args.IndexOf("--operation") + 1].ShouldBe("Q");
        args.ShouldContain("--var");
        args[args.IndexOf("--var") + 1].ShouldBe("a=1");
        args.ShouldContain("--raw");
        args.ShouldContain("--strict-selection");
        args.ShouldContain("--emit-defaults");

        // The document is the trailing positional argument.
        args[^1].ShouldBe("query Q($a: Int) { x }");
    }

    [Fact]
    public void Default_true_toggles_are_only_emitted_when_turned_off()
    {
        CliCommandBuilder.BuildGraphQlArgs(Request(allowUnknownFields: true, introspection: true))
            .ShouldNotContain("--introspection");

        CliCommandBuilder.BuildGraphQlArgs(Request(allowUnknownFields: false, introspection: false))
            .ShouldContain("--introspection");
    }

    [Fact]
    public void The_command_string_starts_with_the_gql2grpc_executable()
        => CliCommandBuilder.BuildGraphQlCommand(Request()).ShouldStartWith("gql2grpc ");
}
