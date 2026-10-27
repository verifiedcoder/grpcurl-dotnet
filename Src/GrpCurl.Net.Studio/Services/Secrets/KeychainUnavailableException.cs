using GrpCurl.Net.Studio.ViewModels.Services;

namespace GrpCurl.Net.Studio.Services.Secrets;

/// <summary>
///     Thrown when the macOS Keychain is unavailable for an operation — locked or access-denied
///     (<c>errSecInteractionNotAllowed</c> / <c>errSecAuthFailed</c>) — as distinct from a functional or
///     otherwise unexpected native failure (a bad query, <c>errSecParam</c>, an interop regression, …).
///     The shared availability signal lets the router fail closed without classifying arbitrary
///     InvalidOperationException instances as permission to change storage.
/// </summary>
internal sealed class KeychainUnavailableException(int status, string message)
    : SecretBackendUnavailableException(message)
{
    public int Status { get; } = status;
}
