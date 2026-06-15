namespace GrpCurl.Net.Utilities;

/// <summary>
///     Detects proxy environment variables that <see cref="System.Net.Http.SocketsHttpHandler" />
///     honours implicitly (<c>HTTP_PROXY</c>/<c>HTTPS_PROXY</c>/<c>ALL_PROXY</c>). A gRPC
///     call routed through an unexpected proxy fails with an error that mentions neither
///     the proxy nor the variable, so connection-failure messages use this to add a hint.
///     Detection only — channel behaviour is unchanged.
/// </summary>
internal static class ProxyEnvironment
{
    // Each proxy variable is conventionally honoured in either upper- or lower-case form.
    // We report one canonical (upper-case) name per concept so the hint is deterministic
    // across platforms: Windows env-var lookups are case-insensitive (both forms resolve to
    // the same value), whereas Linux/macOS are case-sensitive (only the set form resolves).
    private static readonly (string Canonical, string[] Forms)[] ProxyVariables =
    [
        ("HTTP_PROXY", ["HTTP_PROXY", "http_proxy"]),
        ("HTTPS_PROXY", ["HTTPS_PROXY", "https_proxy"]),
        ("ALL_PROXY", ["ALL_PROXY", "all_proxy"])
    ];

    /// <summary>
    ///     Returns the canonical names of proxy variables currently set that could affect a
    ///     call to <paramref name="address" />. Empty when none are set, when the target host
    ///     is excluded via <c>NO_PROXY</c>, or when the address is a Unix domain socket
    ///     (which never routes through an HTTP proxy).
    /// </summary>
    public static IReadOnlyList<string> GetActiveProxyVariables(string? address)
    {
        if (address is not null && GrpcChannelFactory.TryExtractUnixSocketPath(address) is not null)
        {
            return [];
        }

        var active = ProxyVariables
            .Where(v => v.Forms.Any(form => !string.IsNullOrEmpty(Environment.GetEnvironmentVariable(form))))
            .Select(v => v.Canonical)
            .ToList();

        if (active.Count == 0)
        {
            return [];
        }

        var host = ExtractHost(address);

        if (host is not null && IsExcludedByNoProxy(host))
        {
            return [];
        }

        return active;
    }

    internal static string? ExtractHost(string? address)
    {
        if (string.IsNullOrWhiteSpace(address))
        {
            return null;
        }

        var candidate = address.Contains("://", StringComparison.Ordinal) ? address : $"http://{address}";

        return Uri.TryCreate(candidate, UriKind.Absolute, out var uri) && !string.IsNullOrEmpty(uri.Host)
            ? uri.Host
            : null;
    }

    internal static bool IsExcludedByNoProxy(string host)
    {
        var noProxy = Environment.GetEnvironmentVariable("NO_PROXY")
                      ?? Environment.GetEnvironmentVariable("no_proxy");

        if (string.IsNullOrWhiteSpace(noProxy))
        {
            return false;
        }

        foreach (var rawEntry in noProxy.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (rawEntry == "*")
            {
                return true;
            }

            // Conventional NO_PROXY matching: exact host, or domain suffix
            // (".example.com" and "example.com" both match "svc.example.com").
            var entry = rawEntry.TrimStart('.');

            if (host.Equals(entry, StringComparison.OrdinalIgnoreCase)
                || host.EndsWith($".{entry}", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
