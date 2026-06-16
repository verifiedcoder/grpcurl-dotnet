using System.CommandLine;
using GrpCurl.Net.Commands;
using GrpCurl.Net.Studio.ViewModels.Connections;
using GrpCurl.Net.Studio.ViewModels.Models;
using GrpCurl.Net.Studio.ViewModels.Models.Connections;
using GrpCurl.Net.Studio.ViewModels.Models.Invocation;
using GrpCurl.Net.Studio.ViewModels.Services;

namespace GrpCurl.Net.Studio.Tests.Unit;

public sealed class CliCommandBuilderTests
{
    private static SavedConnection Conn(TransportMode transport = TransportMode.Plaintext) => new()
    {
        Name = "c",
        Address = "host:443",
        Transport = transport
    };

    [Fact]
    public void Builds_a_plaintext_invoke_command()
    {
        var request = new InvocationRequestModel(Conn(), "pkg.Svc/Go", "{}", [], Deadline: "10s", AllowUnknownFields: false);

        CliCommandBuilder.BuildCommand(request)
            .ShouldBe("grpcn invoke --plaintext --max-time 10s -d '{}' host:443 pkg.Svc/Go");
    }

    [Fact]
    public void Secret_header_values_become_placeholders_never_literals()
    {
        var request = new InvocationRequestModel(
            Conn(), "p.S/M", "{}",
            [new HeaderEntry { Name = "authorization", Value = "Bearer abc123" }],
            AllowUnknownFields: false);

        var command = CliCommandBuilder.BuildCommand(request);

        command.ShouldContain("--rpc-header");
        command.ShouldContain("${AUTHORIZATION}");
        command.ShouldNotContain("Bearer");
        command.ShouldNotContain("abc123");
    }

    [Fact]
    public void Generated_command_round_trips_through_the_real_cli_parser()
    {
        var connection = new SavedConnection
        {
            Name = "c",
            Address = "host:443",
            Transport = TransportMode.Plaintext,
            UserAgent = "studio/1.0",
            Authority = "example.test"
        };
        var request = new InvocationRequestModel(
            connection, "pkg.Svc/Go", "{ \"a\": 1 }",
            [new HeaderEntry { Name = "x-test", Value = "v" }, new HeaderEntry { Name = "authorization", Value = "secret" }],
            Deadline: "30s", EmitDefaults: true, AllowUnknownFields: true, MaxMessageSize: "4MB");

        var root = new RootCommand();
        root.Subcommands.Add(InvokeCommandHandler.Create());

        var parse = root.Parse(CliCommandBuilder.BuildArgs(request).ToArray());

        parse.Errors.ShouldBeEmpty(string.Join("; ", parse.Errors.Select(e => e.Message)));
    }

    [Theory]
    [InlineData("authorization", true)]
    [InlineData("x-api-token", true)]
    [InlineData("x-custom", false)]
    public void Header_rows_flag_secret_names(string name, bool secret)
        => new HeaderRowViewModel { Name = name }.IsSecret.ShouldBe(secret);

    [Fact]
    public void Powershell_dialect_quotes_json_with_doubled_single_quotes()
    {
        var request = new InvocationRequestModel(Conn(), "p.S/M", "{ \"a\": 1 }", [], AllowUnknownFields: false);

        var command = CliCommandBuilder.BuildCommand(request, ShellDialect.PowerShell);

        command.ShouldContain("'{ \"a\": 1 }'");   // PowerShell single-quote literal
    }

    [Fact]
    public void Cmd_dialect_quotes_json_with_double_quotes()
    {
        var request = new InvocationRequestModel(Conn(), "p.S/M", "{ \"a\": 1 }", [], AllowUnknownFields: false);

        var command = CliCommandBuilder.BuildCommand(request, ShellDialect.Cmd);

        command.ShouldContain("\"{ \"\"a\"\": 1 }\"");   // cmd doubles inner quotes
    }

    [Fact]
    public void Default_dialect_is_bash()
    {
        var request = new InvocationRequestModel(Conn(), "p.S/M", "{}", [], AllowUnknownFields: false);

        CliCommandBuilder.BuildCommand(request).ShouldBe(CliCommandBuilder.BuildCommand(request, ShellDialect.Bash));
    }
}
