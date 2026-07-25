namespace GrpCurl.Net.Studio.Services.Secrets;

/// <summary>
///     Thrown when the macOS Keychain is unavailable for an operation — locked or access-denied
///     (<c>errSecInteractionNotAllowed</c> / <c>errSecAuthFailed</c>) — as distinct from a functional or
///     otherwise unexpected native failure (a bad query, <c>errSecParam</c>, an interop regression, …).
///     It derives from <see cref="InvalidOperationException" /> so <c>SecretStore.IsNativeFailure</c> still
///     catches it and falls back to the encrypted-file store unchanged; the distinct type is what lets a
///     caller act only on genuine unavailability (e.g. a test availability probe that skips when locked)
///     while every other native error propagates instead of being silently masked.
/// </summary>
internal sealed class KeychainUnavailableException(int status, string message)
    : InvalidOperationException(message)
{
    public int Status { get; } = status;
}
