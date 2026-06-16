namespace GrpCurl.Net.Studio.ViewModels.Connections;

/// <summary>
///     The three server-validation choices a TLS profile offers (FR-031). Maps onto the model fields:
///     <see cref="CustomCa" /> sets <c>CaCertPath</c>, <see cref="SkipVerification" /> sets
///     <c>InsecureSkipVerify</c>, and <see cref="SystemRoots" /> sets neither.
/// </summary>
public enum TlsValidationMode
{
    /// <summary>Validate against the OS trust store (default, secure).</summary>
    SystemRoots,

    /// <summary>Validate against a custom CA PEM file (CLI <c>--cacert</c>).</summary>
    CustomCa,

    /// <summary>Skip certificate validation entirely (CLI <c>--insecure</c>) — gated and loud.</summary>
    SkipVerification
}
