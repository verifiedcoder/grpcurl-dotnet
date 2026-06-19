using GrpCurl.Net.Commands;
using GrpCurl.Net.Studio.ViewModels.Connections;
using GrpCurl.Net.Studio.ViewModels.Models;
using GrpCurl.Net.Studio.ViewModels.Models.Connections;
using GrpCurl.Net.Studio.ViewModels.Models.Invocation;
using GrpCurl.Net.Studio.ViewModels.Services;
using System.CommandLine;

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

    // ── FR-165: streaming copy-as-CLI emits a runnable -d '[…]' array ─────────

    [Fact]
    public void Streaming_command_emits_a_runnable_json_array()
    {
        var request = new InvocationRequestModel(Conn(), "p.Chat/Stream", "{}", [], AllowUnknownFields: false);

        var command = CliCommandBuilder.BuildStreamingCommand(request, ["{ \"a\": 1 }", "{ \"b\": 2 }"]);

        // A single pasteable line — no comment, no loose -d lines — with the messages as a JSON array.
        command.ShouldNotContain("\n");
        command.ShouldStartWith("grpcn invoke");
        command.ShouldContain("-d '[{ \"a\": 1 },{ \"b\": 2 }]'");
        command.ShouldEndWith("p.Chat/Stream");
    }

    [Fact]
    public void Streaming_command_skips_blank_messages()
    {
        var request = new InvocationRequestModel(Conn(), "p.Chat/Stream", "{}", [], AllowUnknownFields: false);

        var command = CliCommandBuilder.BuildStreamingCommand(request, ["{\"a\":1}", "   ", ""]);

        command.ShouldContain("-d '[{\"a\":1}]'");
    }

    // ── B4: TLS-profile material renders as matching CLI flags (secrets as ${VAR}) ──

    [Fact]
    public void Tls_profile_renders_matching_cli_flags()
    {
        var connection = new SavedConnection { Name = "c", Address = "host:443", Transport = TransportMode.Tls };
        var request = new InvocationRequestModel(connection, "p.S/M", "{}", [], AllowUnknownFields: false);

        var profile = new TlsProfile
        {
            CaCertPath = "/etc/ca.pem",
            ClientCertPath = "/etc/client.pem",
            ClientKeyPath = "/etc/client.key",
            ClientCertPasswordSecretRef = "secret-ref-1",
            RevocationMode = "nocheck",
            ExportableClientKey = true
        };

        var command = CliCommandBuilder.BuildCommand(request, ShellDialect.Bash, profile);

        command.ShouldContain("--cacert /etc/ca.pem");
        command.ShouldContain("--cert /etc/client.pem");
        command.ShouldContain("--key /etc/client.key");
        command.ShouldContain("--revocation-mode nocheck");
        command.ShouldContain("--exportable-key");
        // FR-161: the secret password is a placeholder, never the secret ref or value.
        command.ShouldContain("--cert-password '${CLIENT_CERT_PASSWORD}'");
        command.ShouldNotContain("secret-ref-1");
    }

    [Fact]
    public void Insecure_tls_profile_emits_insecure_flag()
    {
        var connection = new SavedConnection { Name = "c", Address = "host:443", Transport = TransportMode.Tls };
        var request = new InvocationRequestModel(connection, "p.S/M", "{}", [], AllowUnknownFields: false);

        var command = CliCommandBuilder.BuildCommand(request, ShellDialect.Bash, new TlsProfile { InsecureSkipVerify = true });

        command.ShouldContain("--insecure");
    }

    [Fact]
    public void Tls_command_with_profile_round_trips_through_the_real_cli_parser()
    {
        var connection = new SavedConnection { Name = "c", Address = "host:443", Transport = TransportMode.Tls };
        var request = new InvocationRequestModel(connection, "p.S/M", "{}", [], AllowUnknownFields: false);
        var profile = new TlsProfile
        {
            CaCertPath = "/etc/ca.pem",
            ClientCertPath = "/etc/client.pem",
            ClientKeyPath = "/etc/client.key",
            ClientCertPasswordSecretRef = "ref",
            RevocationMode = "nocheck",
            ExportableClientKey = true
        };

        var root = new RootCommand();
        root.Subcommands.Add(InvokeCommandHandler.Create());

        var parse = root.Parse(CliCommandBuilder.BuildArgs(request, profile).ToArray());

        parse.Errors.ShouldBeEmpty(string.Join("; ", parse.Errors.Select(e => e.Message)));
    }

    [Fact]
    public void Plaintext_connection_ignores_a_tls_profile()
    {
        var request = new InvocationRequestModel(Conn(), "p.S/M", "{}", [], AllowUnknownFields: false);

        var command = CliCommandBuilder.BuildCommand(request, ShellDialect.Bash, new TlsProfile { CaCertPath = "/etc/ca.pem" });

        command.ShouldNotContain("--cacert");
    }
}
