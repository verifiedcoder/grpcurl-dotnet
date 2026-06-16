using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using GrpCurl.Net.Studio.ViewModels.Services;

namespace GrpCurl.Net.Studio.Services.Secrets;

/// <summary>
///     Linux secret backend over <c>libsecret</c> (the Secret Service / GNOME Keyring). Secrets are
///     keyed by a single <c>keyref</c> attribute under a private schema. Any native failure (library
///     absent, no keyring/D-Bus session — e.g. on a headless box) throws so the facade falls back to
///     the encrypted-file store.
/// </summary>
[SupportedOSPlatform("linux")]
internal sealed class LinuxLibsecretSecretStore : ISecretStore
{
    private const string Lib = "libsecret-1.so.0";
    private const string SchemaName = "org.grpcurl.studio.Secret";
    private const string AttributeName = "keyref";

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
            secret_password_clear_sync(ref schema, IntPtr.Zero, out var error, AttributeName, keyRef, IntPtr.Zero);
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

        if (error != IntPtr.Zero)
        {
            g_error_free(error);
        }

        throw new InvalidOperationException($"libsecret {op} failed.");
    }

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

    [DllImport(Lib, CharSet = CharSet.Ansi)]
    private static extern int secret_password_store_sync(ref SecretSchema schema, string? collection, string label, string password, IntPtr cancellable, out IntPtr error, string attr, string value, IntPtr terminator);

    [DllImport(Lib, CharSet = CharSet.Ansi)]
    private static extern IntPtr secret_password_lookup_sync(ref SecretSchema schema, IntPtr cancellable, out IntPtr error, string attr, string value, IntPtr terminator);

    [DllImport(Lib, CharSet = CharSet.Ansi)]
    private static extern int secret_password_clear_sync(ref SecretSchema schema, IntPtr cancellable, out IntPtr error, string attr, string value, IntPtr terminator);

    [DllImport(Lib)]
    private static extern void secret_password_free(IntPtr password);

    [DllImport("libglib-2.0.so.0")]
    private static extern void g_error_free(IntPtr error);
}
