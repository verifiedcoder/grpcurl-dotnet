namespace GrpCurl.Net.Studio.ViewModels.Services;

/// <summary>Explicit, session-scoped secret backend selection (PRD-006).</summary>
public interface ISecretStoreControl
{
    SecretStoreState State { get; }

    event EventHandler? StateChanged;

    Task EnableFallbackAsync(CancellationToken cancellationToken = default);

    Task RetryNativeAsync(CancellationToken cancellationToken = default);
}

public sealed record SecretStoreState(
    SecretStoreInfo Info,
    string Message,
    bool CanEnableFallback,
    bool CanRetryNative,
    string FallbackLimitation);

/// <summary>
///     Expected storage unavailability, including policy denial. Messages are safe for UI/diagnostics;
///     they contain neither credentials nor raw native error text. Missing keys are a separate result.
/// </summary>
public class SecretBackendUnavailableException(string message) : InvalidOperationException(message);
