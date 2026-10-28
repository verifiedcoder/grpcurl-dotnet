using GrpCurl.Net.Studio.ViewModels.Services;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace GrpCurl.Net.Studio.Services.Secrets;

/// <summary>
///     Linux secret backend over <c>libsecret</c> (the Secret Service / GNOME Keyring). Secrets are
///     keyed by a single <c>keyref</c> attribute under a private schema. Any native failure (library
///     absent, no keyring/D-Bus session — e.g. on a headless box) fails the operation. Only documented
///     availability errors carry the typed signal used by the consent policy; unknown failures do not.
/// </summary>
[SupportedOSPlatform("linux")]
internal sealed class LinuxLibsecretSecretStore : ISecretBackend
{
    private const string Lib = "libsecret-1.so.0";
    private const string SchemaName = "org.grpcurl.studio.Secret";
    private const string AttributeName = "keyref";

    public SecretStoreInfo Info { get; } = new("Secret Service (libsecret)", IsOsKeychain: true, LimitationNote: null);

    public async Task<bool> ExistsAsync(string keyRef, CancellationToken cancellationToken = default)
        => await GetAsync(keyRef, cancellationToken).ConfigureAwait(false) is not null;

    public Task SetAsync(string keyRef, string value, CancellationToken cancellationToken = default)
    {
        WithSchema(schema =>
        {
            var ok = secret_password_store_sync(ref schema, null, $"GrpCurl.Net Studio ({keyRef})", value, IntPtr.Zero, out var error, AttributeName, keyRef, IntPtr.Zero);
            ThrowIfFailed(ok != 0, error, "store");
        });

        return Task.CompletedTask;
    }

    public Task<string?> GetAsync(string keyRef, CancellationToken cancellationToken = default)
    {
        string? result = null;

        WithSchema(schema =>
        {
            var ptr = secret_password_lookup_sync(ref schema, IntPtr.Zero, out var error, AttributeName, keyRef, IntPtr.Zero);

            if (error != IntPtr.Zero)
            {
                ThrowIfFailed(condition: false, error, "lookup");
            }

            if (ptr != IntPtr.Zero)
            {
                result = Marshal.PtrToStringUTF8(ptr);
                secret_password_free(ptr);
            }
        });

        return Task.FromResult(result);
    }

    public Task DeleteAsync(string keyRef, CancellationToken cancellationToken = default)
    {
        WithSchema(schema =>
        {
            _ = secret_password_clear_sync(ref schema, IntPtr.Zero, out var error, AttributeName, keyRef, IntPtr.Zero);
            if (error != IntPtr.Zero)
            {
                ThrowIfFailed(condition: false, error, "clear");
            }
        });

        return Task.CompletedTask;
    }

    private static void WithSchema(Action<SecretSchema> action)
    {
        var attributes = new SecretSchemaAttribute[32];
        var schemaNamePtr = Marshal.StringToCoTaskMemUTF8(SchemaName);
        var attrNamePtr = Marshal.StringToCoTaskMemUTF8(AttributeName);
        attributes[0] = new SecretSchemaAttribute { Name = attrNamePtr, Type = 0 };

        try
        {
            action(new SecretSchema { Name = schemaNamePtr, Flags = 0, Attributes = attributes });
        }
        finally
        {
            Marshal.FreeCoTaskMem(schemaNamePtr);
            Marshal.FreeCoTaskMem(attrNamePtr);
        }
    }

    private static void ThrowIfFailed(bool condition, IntPtr error, string op)
    {
        if (condition && error == IntPtr.Zero)
        {
            return;
        }

        try
        {
            if (error != IntPtr.Zero)
            {
                var native = Marshal.PtrToStructure<NativeError>(error);
                if (LibsecretErrorMapping.IsUnavailable(native.Domain, native.Code, secret_error_get_quark(), g_dbus_error_quark(), g_io_error_quark()))
                {
                    throw new SecretBackendUnavailableException("Secret Service is locked or unavailable. Unlock the keyring or start a Secret Service provider.");
                }
            }
        }
        finally
        {
            if (error != IntPtr.Zero)
            {
                g_error_free(error);
            }
        }

        throw new InvalidOperationException($"libsecret {op} failed.");
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeError
    {
        public uint Domain;
        public int Code;
        public IntPtr Message; // deliberately never decoded: native text can contain sensitive context
    }

    [DllImport(Lib)]
    private static extern uint secret_error_get_quark();

    [DllImport("libgio-2.0.so.0")]
    private static extern uint g_dbus_error_quark();

    [DllImport("libgio-2.0.so.0")]
    private static extern uint g_io_error_quark();

    [StructLayout(LayoutKind.Sequential)]
    private struct SecretSchemaAttribute
    {
        public IntPtr Name;
        public int Type;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SecretSchema
    {
        public IntPtr Name;
        public int Flags;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)]
        public SecretSchemaAttribute[] Attributes;
    }

    // BestFitMapping=false + ThrowOnUnmappableChar=true (CA2101): on Ansi string marshaling, disable
    // best-fit character mapping (a homoglyph-substitution hazard) and fail loudly rather than silently
    // corrupting a secret with an unmappable character. On Unix the Ansi charset marshals as UTF-8.
    [DllImport(Lib, CharSet = CharSet.Ansi, BestFitMapping = false, ThrowOnUnmappableChar = true)]
    private static extern int secret_password_store_sync(ref SecretSchema schema, string? collection, string label, string password, IntPtr cancellable, out IntPtr error, string attr, string value, IntPtr terminator);

    [DllImport(Lib, CharSet = CharSet.Ansi, BestFitMapping = false, ThrowOnUnmappableChar = true)]
    private static extern IntPtr secret_password_lookup_sync(ref SecretSchema schema, IntPtr cancellable, out IntPtr error, string attr, string value, IntPtr terminator);

    [DllImport(Lib, CharSet = CharSet.Ansi, BestFitMapping = false, ThrowOnUnmappableChar = true)]
    private static extern int secret_password_clear_sync(ref SecretSchema schema, IntPtr cancellable, out IntPtr error, string attr, string value, IntPtr terminator);

    [DllImport(Lib)]
    private static extern void secret_password_free(IntPtr password);

    [DllImport("libglib-2.0.so.0")]
    private static extern void g_error_free(IntPtr error);
}

// Pure mapping, testable on every OS. Native error text is deliberately never classified or logged.
internal static class LibsecretErrorMapping
{
    // https://gnome.pages.gitlab.gnome.org/libsecret/enum.Error.html: IS_LOCKED.
    // https://docs.gtk.org/gio/error.DBusError.html: bus transport, access and SPAWN_* failures.
    // https://docs.gtk.org/gio/error.IOErrorEnum.html: session-bus address/socket failures. In particular,
    // GIO reports FAILED for no headless session bus and INVALID_ARGUMENT for an empty bus address.
    // Protocol/schema errors, cancellation and unknown codes/domains remain operation failures.
    internal static bool IsUnavailable(uint domain, int code, uint secretDomain, uint dbusDomain, uint ioDomain)
        => domain == secretDomain && code == 2
           || domain == dbusDomain && code is 2 or 3 or 4 or 5 or 6 or 7 or 9 or 10 or 11 or 12 or 15 or 20 or >= 23 and <= 34
           || domain == ioDomain && code is 0 or 1 or 13 or 14 or 15 or 18 or 24 or 39 or 44 or 45;
}
