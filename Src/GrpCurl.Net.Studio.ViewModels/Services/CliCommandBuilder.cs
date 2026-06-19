using GrpCurl.Net.Studio.ViewModels.Models;
using GrpCurl.Net.Studio.ViewModels.Models.Connections;
using GrpCurl.Net.Studio.ViewModels.Models.GraphQl;
using GrpCurl.Net.Studio.ViewModels.Models.Invocation;
using GrpCurl.Net.Utilities;
using System.Text;
using System.Text.Json.Nodes;

namespace GrpCurl.Net.Studio.ViewModels.Services;

/// <summary>
///     Renders a tab's state as an equivalent <c>grpcn invoke</c> command (FR-160). Secret-typed
///     header values (per Core's <see cref="SecretRedactor" />) are emitted as <c>${VAR}</c>
///     placeholders, never as literals (FR-161). <see cref="BuildArgs" /> yields the raw argument
///     list (used to verify the command round-trips through the real CLI parser, FR-162);
///     <see cref="BuildCommand" /> yields a shell-pasteable string.
/// </summary>
public static class CliCommandBuilder
{
    /// <summary>
    ///     The argument list for <c>grpcn</c>, starting at the <c>invoke</c> subcommand. <paramref name="tlsProfile" />
    ///     is the connection's resolved TLS profile (FR-012); when present on a TLS connection its material is
    ///     emitted as the matching CLI flags so the copied command validates the same way Studio does (FR-160).
    /// </summary>
    public static IReadOnlyList<string> BuildArgs(InvocationRequestModel request, TlsProfile? tlsProfile = null)
        => BuildArgsCore(request, tlsProfile, request.RequestJson);

    private static List<string> BuildArgsCore(InvocationRequestModel request, TlsProfile? tlsProfile, string? dataPayload)
    {
        var connection = request.Connection;
        var args = new List<string> { "invoke" };

        if (connection.Transport == TransportMode.Plaintext)
        {
            args.Add("--plaintext");
        }

        AddValue(args, "--connect-timeout", connection.ConnectTimeout);
        AddValue(args, "--keepalive-time", connection.Keepalive.Time);
        AddValue(args, "--keepalive-timeout", connection.Keepalive.Timeout);
        AddValue(args, "--authority", connection.Authority);

        if (connection.Transport == TransportMode.Tls)
        {
            AddValue(args, "--servername", connection.ServerName);
            AddTlsFlags(args, tlsProfile);
        }

        AddValue(args, "--user-agent", connection.UserAgent);

        foreach (var header in connection.ReflectionHeaders)
        {
            AddHeader(args, "--reflect-header", header.Name, header.Value);
        }

        foreach (var header in request.Headers)
        {
            AddHeader(args, "--rpc-header", header.Name, header.Value);
        }

        AddValue(args, "--max-time", request.Deadline);

        if (request.EmitDefaults)
        {
            args.Add("--emit-defaults");
        }

        if (request.AllowUnknownFields)
        {
            args.Add("--allow-unknown-fields");
        }

        AddValue(args, "--max-msg-sz", request.MaxMessageSize);

        // FR-062: reproduce the request-body grammar so the pasted command parses the same body.
        if (request.BodyFormat == RequestBodyFormat.Text)
        {
            args.Add("--format");
            args.Add("text");
        }

        if (dataPayload is not null)
        {
            args.Add("-d");
            args.Add(dataPayload);
        }

        args.Add(connection.Address);
        args.Add(request.MethodSymbol);

        return args;
    }

    // FR-160/161: mirror ConnectionChannelMapper's TLS-profile mapping as CLI flags. Certificate/key
    // material is a file path (safe to emit), but the client-cert password is a secret reference, so it
    // becomes a ${VAR} placeholder — never the resolved value.
    private static void AddTlsFlags(List<string> args, TlsProfile? profile)
    {
        if (profile is null)
        {
            return;
        }

        if (profile.InsecureSkipVerify)
        {
            args.Add("--insecure");
        }

        AddValue(args, "--cacert", profile.CaCertPath);
        AddValue(args, "--cert", profile.ClientCertPath);
        AddValue(args, "--key", profile.ClientKeyPath);

        if (!string.IsNullOrWhiteSpace(profile.ClientCertPasswordSecretRef))
        {
            args.Add("--cert-password");
            args.Add("${CLIENT_CERT_PASSWORD}");
        }

        AddValue(args, "--revocation-mode", profile.RevocationMode);

        if (profile.ExportableClientKey)
        {
            args.Add("--exportable-key");
        }
    }

    /// <summary>A single shell-pasteable <c>grpcn invoke …</c> command line for the given dialect (FR-163).</summary>
    public static string BuildCommand(
        InvocationRequestModel request, ShellDialect dialect = ShellDialect.Bash, TlsProfile? tlsProfile = null)
        => Render("grpcn", BuildArgsCore(request, tlsProfile, request.RequestJson), dialect);

    /// <summary>
    ///     FR-165: a streaming tab's equivalent command. Client/bidi messages are emitted as a single
    ///     <c>-d '[{…},{…}]'</c> JSON array — the CLI's documented streaming-input grammar — so the copied
    ///     command is runnable exactly as pasted, rather than a comment followed by loose <c>-d</c> lines
    ///     that no shell would execute.
    /// </summary>
    public static string BuildStreamingCommand(
        InvocationRequestModel request,
        IReadOnlyList<string> messages,
        ShellDialect dialect = ShellDialect.Bash,
        TlsProfile? tlsProfile = null)
        => Render("grpcn", BuildArgsCore(request, tlsProfile, BuildStreamingPayload(messages)), dialect);

    private static string Render(string executable, IReadOnlyList<string> args, ShellDialect dialect)
    {
        var sb = new StringBuilder(executable);

        foreach (var arg in args)
        {
            _ = sb.Append(' ').Append(ShellQuote(arg, dialect));
        }

        return sb.ToString();
    }

    private static string BuildStreamingPayload(IReadOnlyList<string> messages)
    {
        // Each composed message is already a JSON value; wrap them in an array — the CLI's client/bidi
        // streaming-input grammar (`--data '[{…},{…}]'`).
        var items = messages.Select(m => m.Trim()).Where(m => m.Length > 0);

        return "[" + string.Join(",", items) + "]";
    }

    // ── gql2grpc (GraphQL tab) — GQL-028 ─────────────────────────────────────

    /// <summary>
    ///     The argument list for a <c>gql2grpc</c> command equivalent to the GraphQL tab's state (GQL-028):
    ///     transport/TLS from the connection, headers (secrets as <c>${VAR}</c>), <c>--default-service</c>/
    ///     <c>--mapping</c>/<c>--operation</c>, scalar <c>--var</c> pairs, output toggles, the address, and the
    ///     document inline. Round-trips through the real <c>gql2grpc</c> parser (verified in tests).
    /// </summary>
    public static IReadOnlyList<string> BuildGraphQlArgs(GraphQlExecutionRequest request, TlsProfile? tlsProfile = null)
    {
        var connection = request.Connection;
        var args = new List<string>();

        if (connection.Transport == TransportMode.Plaintext)
        {
            args.Add("--plaintext");
        }

        AddValue(args, "--connect-timeout", connection.ConnectTimeout);
        AddValue(args, "--keepalive-time", connection.Keepalive.Time);
        AddValue(args, "--keepalive-timeout", connection.Keepalive.Timeout);
        AddValue(args, "--authority", connection.Authority);

        if (connection.Transport == TransportMode.Tls)
        {
            AddValue(args, "--servername", connection.ServerName);
            AddTlsFlags(args, tlsProfile);
        }

        AddValue(args, "--user-agent", connection.UserAgent);

        foreach (var header in connection.ReflectionHeaders)
        {
            AddHeader(args, "--reflect-header", header.Name, header.Value);
        }

        foreach (var header in request.Headers)
        {
            AddHeader(args, "--rpc-header", header.Name, header.Value);
        }

        AddValue(args, "--max-time", request.Deadline);
        AddValue(args, "--mapping", request.MappingPath);
        AddValue(args, "--default-service", request.DefaultService);
        AddValue(args, "--operation", request.OperationName);

        if (request.VariablesJson is not null)
        {
            AddScalarVariables(args, request.VariablesJson);
        }

        if (request.EmitDefaults)
        {
            args.Add("--emit-defaults");
        }

        if (request.StrictSelection)
        {
            args.Add("--strict-selection");
        }

        if (request.Raw)
        {
            args.Add("--raw");
        }

        // These CLI flags default to true; emit an explicit override only when the tab turned them off.
        if (!request.AllowUnknownFields)
        {
            args.Add("--allow-unknown-fields");
            args.Add("false");
        }

        if (!request.Introspection)
        {
            args.Add("--introspection");
            args.Add("false");
        }

        args.Add(connection.Address);

        if (!string.IsNullOrWhiteSpace(request.Document))
        {
            args.Add(request.Document);
        }

        return args;
    }

    /// <summary>A single shell-pasteable <c>gql2grpc …</c> command line for the GraphQL tab (GQL-028).</summary>
    public static string BuildGraphQlCommand(
        GraphQlExecutionRequest request, ShellDialect dialect = ShellDialect.Bash, TlsProfile? tlsProfile = null)
        => Render("gql2grpc", BuildGraphQlArgs(request, tlsProfile), dialect);

    // Emit each top-level scalar variable as `--var name=value`. Object/array variables can't be expressed
    // via --var (the CLI requires --variables-file for those), so they are skipped here.
    private static void AddScalarVariables(List<string> args, string variablesJson)
    {
        if (string.IsNullOrWhiteSpace(variablesJson))
        {
            return;
        }

        JsonObject? obj;
        try
        {
            obj = JsonNode.Parse(variablesJson) as JsonObject;
        }
        catch (System.Text.Json.JsonException)
        {
            return;
        }

        if (obj is null)
        {
            return;
        }

        foreach (var pair in obj)
        {
            if (pair.Value is JsonValue value)
            {
                args.Add("--var");
                args.Add($"{pair.Key}={value}");
            }
        }
    }

    private static void AddValue(List<string> args, string flag, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            args.Add(flag);
            args.Add(value);
        }
    }

    private static void AddHeader(List<string> args, string flag, string name, string value)
    {
        // FR-161: secret values become ${VAR} placeholders, never literals.
        var rendered = SecretRedactor.ShouldRedact(name) ? $"${{{PlaceholderName(name)}}}" : value;
        args.Add(flag);
        args.Add($"{name}: {rendered}");
    }

    private static string PlaceholderName(string headerName)
    {
        var chars = headerName.ToUpperInvariant().Select(c => char.IsLetterOrDigit(c) ? c : '_');
        return new string(chars.ToArray());
    }

    private static string ShellQuote(string value, ShellDialect dialect)
    {
        if (value.Length > 0 && value.All(c => char.IsLetterOrDigit(c) || c is '-' or '_' or '.' or '/' or ':'))
        {
            return value;
        }

        return dialect switch
        {
            // PowerShell single-quoted literal: ' -> ''
            ShellDialect.PowerShell => "'" + value.Replace("'", "''") + "'",
            // cmd.exe double-quoted: " -> "" (best-effort; cmd quoting is limited)
            ShellDialect.Cmd => "\"" + value.Replace("\"", "\"\"") + "\"",
            // POSIX single-quoting: ' -> '\''
            _ => "'" + value.Replace("'", "'\\''") + "'"
        };
    }
}
