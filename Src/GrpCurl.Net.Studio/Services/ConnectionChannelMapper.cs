using Grpc.Core;
using GrpCurl.Net.Studio.ViewModels.Models.Connections;
using GrpCurl.Net.Utilities;

namespace GrpCurl.Net.Studio.Services;

/// <summary>
///     Translates a <see cref="SavedConnection" /> into the Core channel options and reflection
///     metadata, so a probe or call uses exactly the wire configuration the CLI would for the
///     same fields. When the connection references a <see cref="TlsProfile" /> (resolved by
///     <see cref="ITlsProfileResolver" />, E2.2) its custom-CA / client-cert / revocation material is
///     applied under TLS; with no profile, TLS uses system-default validation.
/// </summary>
internal static class ConnectionChannelMapper
{
    public static GrpcChannelFactory.ChannelOptions ToChannelOptions(
        SavedConnection connection,
        int? maxMessageSize = null,
        TlsProfile? profile = null,
        string? clientCertPassword = null)
    {
        var tls = connection.Transport == TransportMode.Tls;

        // Profile material only has meaning under TLS; a profile attached to a plaintext target is ignored.
        var applyProfile = tls && profile is not null;

        return new GrpcChannelFactory.ChannelOptions
        {
            Plaintext = connection.Transport == TransportMode.Plaintext,
            ConnectTimeout = ParseOrNull(connection.ConnectTimeout),
            KeepaliveTime = ParseOrNull(connection.Keepalive.Time),
            KeepaliveTimeout = ParseOrNull(connection.Keepalive.Timeout),
            Authority = NullIfBlank(connection.Authority),
            // SNI only applies under TLS.
            ServerName = tls ? NullIfBlank(connection.ServerName) : null,
            // Applies to both send and receive limits, mirroring the CLI's --max-msg-sz (FR-071).
            MaxReceiveMessageSize = maxMessageSize,
            MaxSendMessageSize = maxMessageSize,

            // TLS profile material (FR-030..039 / SEC-014..018) — Core stays the single TLS engine.
            InsecureSkipVerify = applyProfile && profile!.InsecureSkipVerify,
            CaCertPath = applyProfile ? NullIfBlank(profile!.CaCertPath) : null,
            ClientCertPath = applyProfile ? NullIfBlank(profile!.ClientCertPath) : null,
            ClientKeyPath = applyProfile ? NullIfBlank(profile!.ClientKeyPath) : null,
            ClientCertPassword = applyProfile ? clientCertPassword : null,
            RevocationMode = applyProfile ? GrpcChannelFactory.ParseRevocationMode(profile!.RevocationMode) : null,
            ExportableClientKey = applyProfile && profile!.ExportableClientKey
        };
    }

    public static Metadata BuildReflectionMetadata(SavedConnection connection)
        => GrpcChannelFactory.CreateMetadata(
            connection.ReflectionHeaders.Select(h => $"{h.Name}: {h.Value}"),
            NullIfBlank(connection.UserAgent));

    /// <summary>
    ///     The descriptor-source path lists Core's <c>DescriptorSourceFactory</c> consumes (FR-040). All
    ///     three are passed regardless of <see cref="DescriptorSourceConfig.Mode" />; Core applies the
    ///     proto &gt; protoset &gt; reflection precedence. The configured mode just decides which lists are
    ///     populated, so a connection can keep, say, protoset paths while reflecting.
    /// </summary>
    public static (IReadOnlyList<string> ProtosetPaths, IReadOnlyList<string> ProtoFiles, IReadOnlyList<string> ImportPaths)
        DescriptorPaths(SavedConnection connection)
    {
        var source = connection.DescriptorSource;

        return source.Mode switch
        {
            DescriptorMode.Protoset => (source.ProtosetPaths, [], []),
            DescriptorMode.Proto => ([], source.ProtoFiles, source.ImportPaths),
            _ => ([], [], [])
        };
    }

    private static TimeSpan? ParseOrNull(string? duration)
        => string.IsNullOrWhiteSpace(duration) ? null : GrpcChannelFactory.ParseDuration(duration);

    private static string? NullIfBlank(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value;
}
