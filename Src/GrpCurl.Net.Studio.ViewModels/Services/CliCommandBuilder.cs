using GrpCurl.Net.Studio.ViewModels.Models;
using GrpCurl.Net.Studio.ViewModels.Models.Connections;
using GrpCurl.Net.Studio.ViewModels.Models.Invocation;
using GrpCurl.Net.Utilities;
using System.Text;

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
        => Render(BuildArgsCore(request, tlsProfile, request.RequestJson), dialect);

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
        => Render(BuildArgsCore(request, tlsProfile, BuildStreamingPayload(messages)), dialect);

    private static string Render(IReadOnlyList<string> args, ShellDialect dialect)
    {
        var sb = new StringBuilder("grpcn");

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
