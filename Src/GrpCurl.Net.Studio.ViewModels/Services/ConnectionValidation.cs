using GrpCurl.Net.Studio.ViewModels.Models.Connections;
using GrpCurl.Net.Utilities;

namespace GrpCurl.Net.Studio.ViewModels.Services;

/// <summary>
///     Pure validation for connection-editor fields (FR-011, FR-013), reusing Core's address
///     and duration grammar so the GUI rejects exactly what the CLI would. No UI dependency.
/// </summary>
public static class ConnectionValidation
{
    /// <summary>
    ///     Validates an address: <c>host:port</c> (port 1–65535), <c>[::1]:port</c> IPv6 literal,
    ///     or <c>unix:///path</c> (rejected on Windows, mirroring Core's fast-fail). Returns null
    ///     when valid, otherwise an error message.
    /// </summary>
    public static string? ValidateAddress(string? address)
    {
        if (string.IsNullOrWhiteSpace(address))
        {
            return "Address is required.";
        }

        var unixPath = GrpcChannelFactory.TryExtractUnixSocketPath(address);

        if (unixPath is not null)
        {
            return OperatingSystem.IsWindows()
                ? "Unix domain sockets are not supported on Windows."
                : null;
        }

        var (host, port, error) = SplitHostPort(address);

        if (error is not null)
        {
            return error;
        }

        if (string.IsNullOrEmpty(host))
        {
            return "Host is required.";
        }

        if (port is null or < 1 or > 65535)
        {
            return "Port must be between 1 and 65535.";
        }

        return null;
    }

    /// <summary>True when the address is a Unix domain socket path (<c>unix:///path</c>); TLS does not apply (FR-011).</summary>
    public static bool IsUnixSocket(string? address)
        => !string.IsNullOrWhiteSpace(address) && GrpcChannelFactory.TryExtractUnixSocketPath(address) is not null;

    /// <summary>Validates an optional CLI duration string (e.g. <c>500ms</c>, <c>10s</c>, <c>1.5m</c>). Empty is allowed.</summary>
    public static string? ValidateDuration(string? duration)
    {
        if (string.IsNullOrWhiteSpace(duration))
        {
            return null;
        }

        try
        {
            GrpcChannelFactory.ParseDuration(duration);
            return null;
        }
        catch (ArgumentException ex)
        {
            return ex.Message;
        }
    }

    /// <summary>True when the whole connection is valid enough to save.</summary>
    public static bool IsConnectionValid(SavedConnection connection)
        => !string.IsNullOrWhiteSpace(connection.Name)
           && ValidateAddress(connection.Address) is null
           && ValidateDuration(connection.ConnectTimeout) is null
           && ValidateDuration(connection.Keepalive.Time) is null
           && ValidateDuration(connection.Keepalive.Timeout) is null;

    private static (string Host, int? Port, string? Error) SplitHostPort(string address)
    {
        // IPv6 literal in brackets: [::1]:port
        if (address.StartsWith('['))
        {
            var close = address.IndexOf(']');

            if (close < 0)
            {
                return (string.Empty, null, "IPv6 literal must be enclosed in [brackets].");
            }

            var hostPart = address[1..close];
            var rest = address[(close + 1)..];

            if (!rest.StartsWith(':'))
            {
                return (string.Empty, null, "IPv6 address must include a :port.");
            }

            return int.TryParse(rest[1..], out var ipv6Port)
                ? (hostPart, ipv6Port, null)
                : (hostPart, null, "Port must be a number.");
        }

        var colon = address.LastIndexOf(':');

        if (colon < 0)
        {
            return (address, null, "Address must include a :port.");
        }

        var host = address[..colon];

        return int.TryParse(address[(colon + 1)..], out var port)
            ? (host, port, null)
            : (host, null, "Port must be a number.");
    }
}
