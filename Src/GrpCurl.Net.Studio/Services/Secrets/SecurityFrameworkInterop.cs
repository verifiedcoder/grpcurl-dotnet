using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;

namespace GrpCurl.Net.Studio.Services.Secrets;

/// <summary>
///     Minimal Security.framework / CoreFoundation P/Invoke surface for generic-password Keychain
///     items (PRD-001). Exists so <see cref="MacKeychainSecretStore" /> never has to hand a secret
///     value to a child process (argv/environment); the value only ever exists as in-process bytes
///     that CoreFoundation copies into a Keychain-owned <c>CFData</c>. Every CF object this class
///     creates is released before the method returns.
/// </summary>
[SupportedOSPlatform("macos")]
internal static class SecurityFrameworkInterop
{
    private const string SecLib = "/System/Library/Frameworks/Security.framework/Security";
    private const string CfLib = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";
    private const uint CFStringEncodingUtf8 = 0x08000100;

    /// <summary>Creates a generic-password item, or updates it in place if one already exists (the
    ///     Security.framework equivalent of the previous CLI's <c>-U</c> flag).</summary>
    internal static int Upsert(string service, string account, byte[] secretUtf8)
    {
        var serviceRef = CreateCfString(service);
        var accountRef = CreateCfString(account);
        var dataRef = IntPtr.Zero;

        try
        {
            dataRef = CFDataCreate(IntPtr.Zero, secretUtf8, secretUtf8.Length);

            var attributes = CreateDictionary(
                [KSecClass, KSecAttrService, KSecAttrAccount, KSecValueData],
                [KSecClassGenericPassword, serviceRef, accountRef, dataRef]);

            int status;
            try
            {
                status = SecItemAdd(attributes, out var added);
                if (added != IntPtr.Zero)
                {
                    CFRelease(added);
                }
            }
            finally
            {
                CFRelease(attributes);
            }

            if (status != KeychainStatusMapping.ErrSecDuplicateItem)
            {
                return status;
            }

            // Duplicate: match the existing item on class/service/account only, and update just its
            // secret data — the query dictionary must not itself carry kSecValueData.
            var query = CreateDictionary(
                [KSecClass, KSecAttrService, KSecAttrAccount],
                [KSecClassGenericPassword, serviceRef, accountRef]);
            var update = CreateDictionary([KSecValueData], [dataRef]);

            try
            {
                return SecItemUpdate(query, update);
            }
            finally
            {
                CFRelease(query);
                CFRelease(update);
            }
        }
        finally
        {
            if (dataRef != IntPtr.Zero)
            {
                CFRelease(dataRef);
            }

            CFRelease(accountRef);
            CFRelease(serviceRef);
        }
    }

    /// <summary>Looks up a generic-password item's secret data. Returns
    ///     <see cref="KeychainStatusMapping.ErrSecItemNotFound" /> (with <paramref name="secretUtf8" />
    ///     left <see langword="null" />) when no such item exists.</summary>
    internal static int TryFind(string service, string account, out byte[]? secretUtf8)
    {
        secretUtf8 = null;
        var serviceRef = CreateCfString(service);
        var accountRef = CreateCfString(account);

        try
        {
            var query = CreateDictionary(
                [KSecClass, KSecAttrService, KSecAttrAccount, KSecReturnData, KSecMatchLimit],
                [KSecClassGenericPassword, serviceRef, accountRef, KCfBooleanTrue, KSecMatchLimitOne]);

            try
            {
                var status = SecItemCopyMatching(query, out var result);
                if (status != KeychainStatusMapping.ErrSecSuccess)
                {
                    return status;
                }

                try
                {
                    var length = (int)CFDataGetLength(result);
                    var buffer = new byte[length];
                    if (length > 0)
                    {
                        Marshal.Copy(CFDataGetBytePtr(result), buffer, 0, length);
                    }

                    secretUtf8 = buffer;
                }
                finally
                {
                    CFRelease(result);
                }

                return KeychainStatusMapping.ErrSecSuccess;
            }
            finally
            {
                CFRelease(query);
            }
        }
        finally
        {
            CFRelease(accountRef);
            CFRelease(serviceRef);
        }
    }

    /// <summary>Deletes a generic-password item. Returns
    ///     <see cref="KeychainStatusMapping.ErrSecItemNotFound" /> (not thrown) when no such item
    ///     exists — callers treat delete as idempotent, matching prior behavior.</summary>
    internal static int Delete(string service, string account)
    {
        var serviceRef = CreateCfString(service);
        var accountRef = CreateCfString(account);

        try
        {
            var query = CreateDictionary(
                [KSecClass, KSecAttrService, KSecAttrAccount],
                [KSecClassGenericPassword, serviceRef, accountRef]);

            try
            {
                return SecItemDelete(query);
            }
            finally
            {
                CFRelease(query);
            }
        }
        finally
        {
            CFRelease(accountRef);
            CFRelease(serviceRef);
        }
    }

    private static IntPtr CreateCfString(string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        return CFStringCreateWithBytes(IntPtr.Zero, bytes, bytes.Length, CFStringEncodingUtf8, isExternalRepresentation: 0);
    }

    private static IntPtr CreateDictionary(IntPtr[] keys, IntPtr[] values)
        => CFDictionaryCreate(IntPtr.Zero, keys, values, keys.Length, KCfTypeDictionaryKeyCallBacks, KCfTypeDictionaryValueCallBacks);

    // Lazily resolved once per process. kSec*/kCFBooleanTrue are CFTypeRef globals: the exported symbol
    // holds a *pointer*, so it must be dereferenced (Marshal.ReadIntPtr) to get the CFStringRef/CFBooleanRef
    // value itself. kCFTypeDictionary*CallBacks are callback *structs*: the exported symbol IS the struct,
    // so its raw address is what CFDictionaryCreate expects — it must NOT be dereferenced.
    private static readonly IntPtr s_secLib = NativeLibrary.Load(SecLib);
    private static readonly IntPtr s_cfLib = NativeLibrary.Load(CfLib);

    private static readonly IntPtr KSecClass = ReadGlobal(s_secLib, "kSecClass");
    private static readonly IntPtr KSecClassGenericPassword = ReadGlobal(s_secLib, "kSecClassGenericPassword");
    private static readonly IntPtr KSecAttrService = ReadGlobal(s_secLib, "kSecAttrService");
    private static readonly IntPtr KSecAttrAccount = ReadGlobal(s_secLib, "kSecAttrAccount");
    private static readonly IntPtr KSecValueData = ReadGlobal(s_secLib, "kSecValueData");
    private static readonly IntPtr KSecReturnData = ReadGlobal(s_secLib, "kSecReturnData");
    private static readonly IntPtr KSecMatchLimit = ReadGlobal(s_secLib, "kSecMatchLimit");
    private static readonly IntPtr KSecMatchLimitOne = ReadGlobal(s_secLib, "kSecMatchLimitOne");
    private static readonly IntPtr KCfBooleanTrue = ReadGlobal(s_cfLib, "kCFBooleanTrue");
    private static readonly IntPtr KCfTypeDictionaryKeyCallBacks = NativeLibrary.GetExport(s_cfLib, "kCFTypeDictionaryKeyCallBacks");
    private static readonly IntPtr KCfTypeDictionaryValueCallBacks = NativeLibrary.GetExport(s_cfLib, "kCFTypeDictionaryValueCallBacks");

    private static IntPtr ReadGlobal(IntPtr library, string symbol) => Marshal.ReadIntPtr(NativeLibrary.GetExport(library, symbol));

    [DllImport(SecLib)]
    private static extern int SecItemAdd(IntPtr attributes, out IntPtr result);

    [DllImport(SecLib)]
    private static extern int SecItemUpdate(IntPtr query, IntPtr attributesToUpdate);

    [DllImport(SecLib)]
    private static extern int SecItemCopyMatching(IntPtr query, out IntPtr result);

    [DllImport(SecLib)]
    private static extern int SecItemDelete(IntPtr query);

    // CoreFoundation's Boolean is a 1-byte unsigned char, not the 4-byte Win32 BOOL that
    // [MarshalAs(UnmanagedType.Bool)] assumes — pass a raw byte (0/1) to avoid that size mismatch.
    [DllImport(CfLib)]
    private static extern IntPtr CFStringCreateWithBytes(IntPtr alloc, byte[] bytes, nint numBytes, uint encoding, byte isExternalRepresentation);

    [DllImport(CfLib)]
    private static extern IntPtr CFDataCreate(IntPtr allocator, byte[] bytes, nint length);

    [DllImport(CfLib)]
    private static extern IntPtr CFDataGetBytePtr(IntPtr theData);

    [DllImport(CfLib)]
    private static extern nint CFDataGetLength(IntPtr theData);

    [DllImport(CfLib)]
    private static extern IntPtr CFDictionaryCreate(IntPtr allocator, IntPtr[] keys, IntPtr[] values, nint numValues, IntPtr keyCallBacks, IntPtr valueCallBacks);

    [DllImport(CfLib)]
    private static extern void CFRelease(IntPtr cf);
}
