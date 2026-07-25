namespace GrpCurl.Net.Studio.Services.Secrets;

/// <summary>
///     Security.framework <c>OSStatus</c> constants and their mapping to exceptions, for PRD-001's
///     macOS Keychain backend (<see cref="SecurityFrameworkInterop" />, <see cref="MacKeychainSecretStore" />).
///     Deliberately <b>not</b> <c>[SupportedOSPlatform("macos")]</c>-gated, even though it is only
///     meaningful on macOS: it is pure int/string logic with no P/Invoke, so the platform-compatibility
///     analyzer leaves it alone and it can be exercised — and unit-tested — on every CI OS. Exception
///     messages carry only the operation name and the numeric status, never a secret value or a keyRef,
///     so logs/exceptions can never disclose a secret.
/// </summary>
internal static class KeychainStatusMapping
{
    internal const int ErrSecSuccess = 0;
    internal const int ErrSecDuplicateItem = -25299;
    internal const int ErrSecItemNotFound = -25300;
    internal const int ErrSecAuthFailed = -25293;
    internal const int ErrSecInteractionNotAllowed = -25308; // keychain locked / UI not allowed

    internal static InvalidOperationException ToException(int status, string operation) => status switch
    {
        ErrSecInteractionNotAllowed or ErrSecAuthFailed
            => new InvalidOperationException($"macOS Keychain is locked or denied access for {operation} (OSStatus {status})."),
        _ => new InvalidOperationException($"macOS Keychain {operation} failed (OSStatus {status}).")
    };
}
