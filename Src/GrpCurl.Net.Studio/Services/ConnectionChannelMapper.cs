using Grpc.Core;
using GrpCurl.Net.Studio.ViewModels.Models.Connections;
using GrpCurl.Net.Utilities;

namespace GrpCurl.Net.Studio.Services;

/// <summary>
///     Translates a <see cref="SavedConnection" /> into the Core channel options and reflection
///     metadata, so a probe or call uses exactly the wire configuration the CLI would for the
///     same fields. TLS profile material (custom CA, client certs) is deferred to E2.2; Phase 1
///     uses system-default validation under TLS.
/// </summary>
internal static class ConnectionChannelMapper
{
    public static GrpcChannelFactory.ChannelOptions ToChannelOptions(SavedConnection connection, int? maxMessageSize = null) => new()
    {
        Plaintext = connection.Transport == TransportMode.Plaintext,
        ConnectTimeout = ParseOrNull(connection.ConnectTimeout),
        KeepaliveTime = ParseOrNull(connection.Keepalive.Time),
        KeepaliveTimeout = ParseOrNull(connection.Keepalive.Timeout),
        Authority = NullIfBlank(connection.Authority),
        // SNI only applies under TLS.
        ServerName = connection.Transport == TransportMode.Tls ? NullIfBlank(connection.ServerName) : null,
        // Applies to both send and receive limits, mirroring the CLI's --max-msg-sz (FR-071).
        MaxReceiveMessageSize = maxMessageSize,
        MaxSendMessageSize = maxMessageSize
    };

    public static Metadata BuildReflectionMetadata(SavedConnection connection)
        => GrpcChannelFactory.CreateMetadata(
            connection.ReflectionHeaders.Select(h => $"{h.Name}: {h.Value}"),
            NullIfBlank(connection.UserAgent));

    private static TimeSpan? ParseOrNull(string? duration)
        => string.IsNullOrWhiteSpace(duration) ? null : GrpcChannelFactory.ParseDuration(duration);

    private static string? NullIfBlank(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value;
}
