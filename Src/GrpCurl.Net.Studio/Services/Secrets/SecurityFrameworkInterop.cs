using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;

namespace GrpCurl.Net.Studio.Services.Secrets;

/// <summary>
///     Minimal Security.framework / CoreFoundation P/Invoke surface for generic-password Keychain
///     items (PRD-001). Exists so <see cref="MacKeychainSecretStore" /> never has to hand a secret
///     value to a child process (argv/environment); the value only ever exists as in-process bytes.
///     On the write path the secret is wrapped with <c>CFDataCreateWithBytesNoCopy</c> over a pinned
///     managed buffer (allocator <c>kCFAllocatorNull</c>) to <i>request</i> that CoreFoundation reference
///     the buffer in place rather than copy it — avoiding the unconditional heap copy <c>CFDataCreate</c>
///     would make and then free (via <c>CFRelease</c>) without zeroing. Apple's contract permits the
///     framework to copy internally anyway, and <c>SecItemAdd</c>/<c>SecItemUpdate</c> necessarily copy the
///     value into the keychain's own protected storage during the call; those framework-internal copies are
///     outside this code's control. The only plaintext buffer this process deterministically owns and can
///     wipe is the caller's managed array, which the caller zeroes. Every CF object this class creates is
///     released before the method returns.
/// </summary>
[SupportedOSPlatform("macos")]
internal sealed class SecurityFrameworkInterop : IKeychainNative
{
    private const string SecLib = "/System/Library/Frameworks/Security.framework/Security";
    private const string CfLib = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";
    private const uint CFStringEncodingUtf8 = 0x08000100;

    /// <summary>Creates a generic-password item, or updates it in place if one already exists (the
    ///     Security.framework equivalent of the previous CLI's <c>-U</c> flag).</summary>
    public int Upsert(string service, string account, byte[] secretUtf8)
    {
        var serviceRef = CreateCfString(service);
        var accountRef = CreateCfString(account);

        // Pin the caller's buffer so CoreFoundation can reference it in place (no-copy) for the duration
        // of the SecItem call. SecItemAdd/SecItemUpdate make their own protected internal copy while they
        // run, so the wrapper does not need to outlive the call.
        var handle = GCHandle.Alloc(secretUtf8, GCHandleType.Pinned);
        var dataRef = IntPtr.Zero;

        try
        {
            dataRef = CFDataCreateWithBytesNoCopy(IntPtr.Zero, handle.AddrOfPinnedObject(), secretUtf8.Length, KCfAllocatorNull);

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
            // Release the CFData while the buffer is still pinned, then unpin. With kCFAllocatorNull the
            // release frees only the CFData wrapper, never the caller's bytes.
            if (dataRef != IntPtr.Zero)
            {
                CFRelease(dataRef);
            }

            CFRelease(accountRef);
            CFRelease(serviceRef);
            handle.Free();
        }
    }

    /// <summary>Looks up a generic-password item's secret data. Returns
    ///     <see cref="KeychainStatusMapping.ErrSecItemNotFound" /> (with <paramref name="secretUtf8" />
    ///     left <see langword="null" />) when no such item exists.</summary>
    public int TryFind(string service, string account, out byte[]? secretUtf8)
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
    public int Delete(string service, string account)
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

    // Lazily resolved once per process. kSec*/kCFBooleanTrue/kCFAllocatorNull are CFTypeRef globals: the
    // exported symbol holds a *pointer*, so it must be dereferenced (Marshal.ReadIntPtr) to get the
    // CFStringRef/CFBooleanRef/CFAllocatorRef value itself. kCFTypeDictionary*CallBacks are callback
    // *structs*: the exported symbol IS the struct, so its raw address is what CFDictionaryCreate expects —
    // it must NOT be dereferenced.
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
    private static readonly IntPtr KCfAllocatorNull = ReadGlobal(s_cfLib, "kCFAllocatorNull");
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
    private static extern IntPtr CFDataCreateWithBytesNoCopy(IntPtr allocator, IntPtr bytes, nint length, IntPtr bytesDeallocator);

    [DllImport(CfLib)]
    private static extern IntPtr CFDataGetBytePtr(IntPtr theData);

    [DllImport(CfLib)]
    private static extern nint CFDataGetLength(IntPtr theData);

    [DllImport(CfLib)]
    private static extern IntPtr CFDictionaryCreate(IntPtr allocator, IntPtr[] keys, IntPtr[] values, nint numValues, IntPtr keyCallBacks, IntPtr valueCallBacks);

    [DllImport(CfLib)]
    private static extern void CFRelease(IntPtr cf);
}
