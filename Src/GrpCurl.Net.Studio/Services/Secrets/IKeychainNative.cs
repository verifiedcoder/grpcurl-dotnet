namespace GrpCurl.Net.Studio.Services.Secrets;

/// <summary>
///     The three native Keychain generic-password operations <see cref="MacKeychainSecretStore" /> needs,
///     expressed as <c>OSStatus</c>-returning calls over already-encoded UTF-8 bytes. Extracted as an
///     interface so the backend's encoding, background execution, buffer zeroing, and OSStatus→exception
///     mapping can be unit-tested on any OS with a fake (PRD-001 error/locked-keychain coverage), while the
///     real Security.framework implementation (<see cref="SecurityFrameworkInterop" />) stays the single
///     macOS-gated type.
/// </summary>
internal interface IKeychainNative
{
    /// <summary>Creates the item, or updates it in place if it already exists.</summary>
    int Upsert(string service, string account, byte[] secretUtf8);

    /// <summary>Reads the item's secret bytes, or returns <c>ErrSecItemNotFound</c> with
    ///     <paramref name="secretUtf8" /> left <see langword="null" />.</summary>
    int TryFind(string service, string account, out byte[]? secretUtf8);

    /// <summary>Removes the item; returns <c>ErrSecItemNotFound</c> (not an error) if it was absent.</summary>
    int Delete(string service, string account);
}
