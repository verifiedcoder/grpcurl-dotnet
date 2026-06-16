namespace GrpCurl.Net.Studio.ViewModels.Models.Connections;

/// <summary>
///     A workspace-level named TLS bundle (FR-030 / SEC-010), referenced by connections via
///     <see cref="SavedConnection.TlsProfileId" />. Mirrors Core's <c>ChannelOptions</c> TLS fields so
///     Studio adds no TLS logic of its own (one-engine principle). Certificate/key material is stored
///     by <em>path only</em>, never copied (SEC-016); the PKCS12 password is the one secret, held in
///     <see cref="ISecretStore" /> and referenced here by <see cref="ClientCertPasswordSecretRef" />
///     (SEC-017) — never a literal.
/// </summary>
public sealed class TlsProfile
{
    public string Id { get; set; } = Guid.NewGuid().ToString();

    public string Name { get; set; } = string.Empty;

    /// <summary>SEC-014: skip certificate validation entirely. Gated + loud in the UI; never a default.</summary>
    public bool InsecureSkipVerify { get; set; }

    /// <summary>FR-031: custom CA PEM file path (system roots when null).</summary>
    public string? CaCertPath { get; set; }

    /// <summary>FR-033: client certificate (PEM cert or a PKCS12 bundle — content-detected by Core).</summary>
    public string? ClientCertPath { get; set; }

    /// <summary>FR-033: client private key PEM (paired with a PEM <see cref="ClientCertPath" />; unused for PKCS12).</summary>
    public string? ClientKeyPath { get; set; }

    /// <summary>SEC-017: secret-store key reference for the PKCS12 password (the workspace never holds the literal).</summary>
    public string? ClientCertPasswordSecretRef { get; set; }

    /// <summary>FR-032: <c>online</c> (default) / <c>offline</c> / <c>nocheck</c>; applies with a custom CA.</summary>
    public string? RevocationMode { get; set; }

    /// <summary>FR-036 / SEC-018: load PKCS12 client keys as exportable (advanced; default off).</summary>
    public bool ExportableClientKey { get; set; }
}
