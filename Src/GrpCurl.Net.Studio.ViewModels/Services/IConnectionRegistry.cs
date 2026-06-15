using GrpCurl.Net.Studio.ViewModels.Models.Connections;

namespace GrpCurl.Net.Studio.ViewModels.Services;

/// <summary>
///     Manages gRPC channel lifecycle for saved connections (SPEC-030 §7). Phase 1 exposes the
///     test-connection probe (FR-018); the cached business channel used by invocation arrives
///     with E1.4. Implementations apply the connection's full <c>GrpcChannelFactory</c> options
///     so a probe exercises the same wire configuration a real call would.
/// </summary>
public interface IConnectionRegistry
{
    /// <summary>
    ///     Probes connectivity. For reflection sources, performs a <c>ListServices</c> round-trip
    ///     with a 10s default deadline and reports the service count; for file-based sources, a
    ///     TCP/TLS handshake only. Cancellable.
    /// </summary>
    Task<TestConnectionResult> TestConnectionAsync(SavedConnection connection, CancellationToken cancellationToken = default);
}
