namespace GrpCurl.Net.Utilities;

/// <summary>
///     Rewrites the outgoing HTTP/2 <c>:authority</c> pseudo-header on every gRPC request so
///     that callers can target virtual-hosted services, ingress gateways, or test rigs that
///     route by authority. Composed into the channel pipeline by
///     <see cref="GrpcChannelFactory" /> when
///     <see cref="GrpcChannelFactory.ChannelOptions.Authority" /> is supplied.
///     <para>
///         <c>HttpRequestMessage.Headers.Host</c> on HTTP/2 becomes <c>:authority</c> on the
///         wire, so writing it here is equivalent to grpcurl's <c>-authority</c> behaviour.
///         TLS SNI is unaffected — that is controlled by
///         <c>SslClientAuthenticationOptions.TargetHost</c>, which
///         <see cref="GrpcChannelFactory" /> still maps from <c>--servername</c>.
///     </para>
/// </summary>
internal sealed class AuthorityOverrideHandler : DelegatingHandler
{
    private readonly string _authority;

    public AuthorityOverrideHandler(string authority, HttpMessageHandler innerHandler)
        : base(innerHandler)
    {
        ArgumentException.ThrowIfNullOrEmpty(authority);

        _authority = authority;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        request.Headers.Host = _authority;

        return base.SendAsync(request, cancellationToken);
    }
}