namespace GrpCurl.Net.Commands;

/// <summary>
///     Drop-in compatibility shim that lets users invoke <c>grpcurl.net</c> with the same
///     positional / single-dash flag shape as upstream <c>grpcurl</c>. Detects the legacy
///     form (single-dash flags, no <c>list</c>/<c>describe</c>/<c>invoke</c> subcommand
///     present) and rewrites the argv into the native subcommand shape before handing
///     off to System.CommandLine. Implements the upstream invocation:
///     <c>grpcurl [flags] host:port [symbol]</c>.
/// </summary>
internal static class GrpcurlCompatHandler
{
    private static readonly HashSet<string> NativeSubcommands = new(StringComparer.Ordinal)
    {
        "list", "describe", "invoke"
    };

    /// <summary>
    ///     Returns the rewritten argv if <paramref name="args"/> looks like an upstream
    ///     grpcurl invocation, otherwise <see langword="null"/> (caller continues with
    ///     native parsing).
    /// </summary>
    public static string[]? TryRewrite(string[] args)
    {
        if (args.Length == 0)
        {
            return null;
        }

        // Any leading native subcommand → leave it alone.
        var firstNonFlag = args.FirstOrDefault(a => !a.StartsWith('-'));

        if (firstNonFlag is not null && NativeSubcommands.Contains(firstNonFlag))
        {
            return null;
        }

        // Help / version → leave to System.CommandLine.
        if (args[0] is "--help" or "-h" or "--version")
        {
            return null;
        }

        // The argv is grpcurl-style when at least one single-dash flag we recognise
        // appears (e.g. -plaintext, -d, -proto). This keeps `grpcurl.net foo` from
        // being misinterpreted as compat mode.
        if (!args.Any(LooksLikeUpstreamFlag))
        {
            return null;
        }

        var rewritten = new List<string>();
        var positionals = new List<string>();

        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];

            if (TryMapFlag(arg, args, ref i, rewritten))
            {
                continue;
            }

            if (!arg.StartsWith('-'))
            {
                positionals.Add(arg);
                continue;
            }

            // Unknown flag → keep verbatim so System.CommandLine surfaces a clear error.
            rewritten.Add(arg);
        }

        // Resolve the subcommand from positionals:
        //  - 0 positionals → list (without an address, fails validation cleanly)
        //  - 1 positional that looks like host:port → list <host:port>
        //  - 2 positionals where the second is "Service/Method" → invoke
        //  - 2 positionals where the second is a symbol → describe
        string subcommand;
        var orderedPositionals = positionals;

        switch (positionals.Count)
        {
            case 0:
                subcommand = "list";
                break;

            case 1:
                subcommand = "list";
                break;

            case 2 when positionals[1].Contains('/'):
                subcommand = "invoke";
                break;

            default:
                subcommand = "describe";
                break;
        }

        var final = new List<string> { subcommand };

        final.AddRange(rewritten);
        final.AddRange(orderedPositionals);

        return [.. final];
    }

    private static bool LooksLikeUpstreamFlag(string arg)
        => arg.Length > 1
           && arg[0] == '-'
           && arg[1] != '-'
           && UpstreamFlagMap.ContainsKey(arg);

    /// <summary>
    ///     Maps a single argv token. Returns <see langword="true"/> when the token was
    ///     consumed (and any associated value advanced through <paramref name="index"/>).
    /// </summary>
    private static bool TryMapFlag(string arg, string[] args, ref int index, List<string> output)
    {
        if (!arg.StartsWith('-') || arg.StartsWith("--"))
        {
            return false;
        }

        // Forms accepted: -flag, -flag=value, -flag value.
        var eq = arg.IndexOf('=');
        var name = eq >= 0 ? arg[..eq] : arg;

        if (!UpstreamFlagMap.TryGetValue(name, out var mapping))
        {
            return false;
        }

        if (mapping.Boolean)
        {
            output.Add(mapping.NativeName);
            return true;
        }

        string value;

        if (eq >= 0)
        {
            value = arg[(eq + 1)..];
        }
        else if (index + 1 < args.Length)
        {
            index++;
            value = args[index];
        }
        else
        {
            // Missing value — let System.CommandLine produce the error.
            output.Add(mapping.NativeName);
            return true;
        }

        output.Add(mapping.NativeName);
        output.Add(value);

        return true;
    }

    private sealed record FlagMapping(string NativeName, bool Boolean);

    /// <summary>
    ///     Upstream-grpcurl single-dash flag → native --double-dash flag.
    /// </summary>
    private static readonly Dictionary<string, FlagMapping> UpstreamFlagMap = new(StringComparer.Ordinal)
    {
        // Booleans
        ["-plaintext"] = new("--plaintext", Boolean: true),
        ["-insecure"] = new("--insecure", Boolean: true),
        ["-emit-defaults"] = new("--emit-defaults", Boolean: true),
        ["-v"] = new("--verbose", Boolean: true),
        ["-vv"] = new("--very-verbose", Boolean: true),
        ["-allow-unknown-fields"] = new("--allow-unknown-fields", Boolean: true),
        ["-unsafe-show-secrets"] = new("--unsafe-show-secrets", Boolean: true),

        // String values
        ["-cacert"] = new("--cacert", Boolean: false),
        ["-cert"] = new("--cert", Boolean: false),
        ["-key"] = new("--key", Boolean: false),
        ["-servername"] = new("--servername", Boolean: false),
        ["-authority"] = new("--authority", Boolean: false),
        ["-user-agent"] = new("--user-agent", Boolean: false),
        ["-d"] = new("--data", Boolean: false),
        ["-data"] = new("--data", Boolean: false),
        ["-format"] = new("--format", Boolean: false),
        ["-max-time"] = new("--max-time", Boolean: false),
        ["-connect-timeout"] = new("--connect-timeout", Boolean: false),
        ["-max-msg-sz"] = new("--max-msg-sz", Boolean: false),
        ["-keepalive-time"] = new("--keepalive-time", Boolean: false),
        ["-keepalive-timeout"] = new("--keepalive-timeout", Boolean: false),
        ["-proto-out-dir"] = new("--proto-out-dir", Boolean: false),
        ["-protoset"] = new("--protoset", Boolean: false),
        ["-protoset-out"] = new("--protoset-out", Boolean: false),
        ["-proto"] = new("--proto", Boolean: false),
        ["-import-path"] = new("--import-path", Boolean: false),
        ["-I"] = new("--import-path", Boolean: false),
        ["-H"] = new("--header", Boolean: false),
        ["-rpc-header"] = new("--rpc-header", Boolean: false),
        ["-reflect-header"] = new("--reflect-header", Boolean: false),
    };
}
