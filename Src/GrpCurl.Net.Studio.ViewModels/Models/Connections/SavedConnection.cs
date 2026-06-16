namespace GrpCurl.Net.Studio.ViewModels.Models.Connections;

/// <summary>Transport for a connection (CLI parity: plaintext selects <c>http://</c>, TLS <c>https://</c>).</summary>
public enum TransportMode
{
    Tls,
    Plaintext
}

/// <summary>
///     How a connection discovers its schema (CLI parity). When more than one is configured the
///     effective source follows Core's <c>DescriptorSourceFactory</c> precedence: proto &gt; protoset
///     &gt; reflection (FR-040).
/// </summary>
public enum DescriptorMode
{
    /// <summary>Server reflection (default; CLI uses the address alone).</summary>
    Reflection,

    /// <summary>Pre-compiled <c>FileDescriptorSet</c> protoset file(s) (CLI <c>--protoset</c>).</summary>
    Protoset,

    /// <summary><c>.proto</c> files compiled via <c>protoc</c> (CLI <c>--proto</c> + <c>-I</c>).</summary>
    Proto
}

/// <summary>
///     A connection's descriptor-source configuration (SPEC-040 §3.2, FR-040..049). Cert/proto/protoset
///     files are referenced <em>by path only</em>, never copied (SEC-016). All three path lists may be
///     populated at once; Core picks the effective source by precedence.
/// </summary>
public sealed class DescriptorSourceConfig
{
    public DescriptorMode Mode { get; set; } = DescriptorMode.Reflection;

    /// <summary>Ordered protoset file paths (CLI <c>--protoset</c>, repeatable).</summary>
    public List<string> ProtosetPaths { get; set; } = [];

    /// <summary>Ordered <c>.proto</c> file paths (CLI <c>--proto</c>, repeatable).</summary>
    public List<string> ProtoFiles { get; set; } = [];

    /// <summary>Ordered import directories passed to <c>protoc</c> (CLI <c>-I</c>/<c>--import-path</c>).</summary>
    public List<string> ImportPaths { get; set; } = [];

    public DescriptorSourceConfig Clone() => new()
    {
        Mode = Mode,
        ProtosetPaths = [.. ProtosetPaths],
        ProtoFiles = [.. ProtoFiles],
        ImportPaths = [.. ImportPaths]
    };
}

/// <summary>A single metadata header entry (name/value, with <c>-bin</c> binary marker).</summary>
public sealed class HeaderEntry
{
    public string Name { get; set; } = string.Empty;

    public string Value { get; set; } = string.Empty;

    /// <summary>True when the header name ends in <c>-bin</c> (binary metadata; value is base64).</summary>
    public bool IsBin { get; set; }
}

/// <summary>Keepalive ping settings (CLI defaults: 60s time, 30s timeout).</summary>
public sealed class KeepaliveSettings
{
    public string? Time { get; set; }

    public string? Timeout { get; set; }
}

/// <summary>
///     A saved gRPC target (SPEC-010 FR-010..019, SPEC-040 §3.2). The unit of identity every
///     invocation binds to. TLS profile references and protoset/proto sources are deferred
///     (E2.2/E2.3); Phase 1 uses TLS system-default validation and reflection.
/// </summary>
public sealed class SavedConnection
{
    public string Id { get; set; } = Guid.NewGuid().ToString();

    public string Name { get; set; } = string.Empty;

    /// <summary>Address with no scheme: <c>host:port</c>, <c>[::1]:port</c>, or <c>unix:///path</c>.</summary>
    public string Address { get; set; } = string.Empty;

    public TransportMode Transport { get; set; } = TransportMode.Tls;

    /// <summary>Connect timeout (CLI duration grammar); null uses the client default.</summary>
    public string? ConnectTimeout { get; set; }

    public KeepaliveSettings Keepalive { get; set; } = new();

    /// <summary>Optional HTTP/2 <c>:authority</c> override (CLI <c>--authority</c>).</summary>
    public string? Authority { get; set; }

    /// <summary>Optional TLS SNI / target host for cert validation (CLI <c>--servername</c>).</summary>
    public string? ServerName { get; set; }

    /// <summary>Optional reference to a workspace <see cref="TlsProfile" /> (FR-012); null = system-default validation.</summary>
    public string? TlsProfileId { get; set; }

    public string? UserAgent { get; set; }

    /// <summary>Headers sent only on server-reflection RPCs (CLI <c>--reflect-header</c>).</summary>
    public List<HeaderEntry> ReflectionHeaders { get; set; } = [];

    /// <summary>Descriptor source: reflection (default), protoset file(s), or proto files + import paths (FR-040).</summary>
    public DescriptorSourceConfig DescriptorSource { get; set; } = new();

    public string? Notes { get; set; }

    public SavedConnection Clone()
    {
        return new SavedConnection
        {
            Id = Guid.NewGuid().ToString(),
            Name = Name,
            Address = Address,
            Transport = Transport,
            ConnectTimeout = ConnectTimeout,
            Keepalive = new KeepaliveSettings { Time = Keepalive.Time, Timeout = Keepalive.Timeout },
            Authority = Authority,
            ServerName = ServerName,
            TlsProfileId = TlsProfileId,
            UserAgent = UserAgent,
            ReflectionHeaders = ReflectionHeaders
                .Select(h => new HeaderEntry { Name = h.Name, Value = h.Value, IsBin = h.IsBin })
                .ToList(),
            DescriptorSource = DescriptorSource.Clone(),
            Notes = Notes
        };
    }
}
