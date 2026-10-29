using GrpCurl.Net.Studio.ViewModels.Services;
using System.ComponentModel;
using System.Runtime.Versioning;
using System.Runtime.InteropServices;
using System.Security;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;

namespace GrpCurl.Net.Studio.Services.Secrets;

// Uses the existing UI error boundaries, but is explicitly excluded from backend-unavailability
// classification: failure to protect/persist a native file must never authorize a downgrade.
internal sealed class SecretStorageException(string message) : SecretBackendUnavailableException(message);

internal enum SecretFileStage
{
    DirectoryCreated, BeforeDirectoryProtection, BeforeFileProtection, BeforeCreate, Created,
    PartialWrite, BeforeFlush, Flushed, BeforeClose, Closed, BeforeReplace, Replaced
}

/// <summary>
///     Same-directory, exclusive, restricted temporary files; replacement is the commit point.
///     Contents are flushed before commit. Directory-entry power-loss durability is filesystem
///     dependent. This is neither a cross-process transaction nor protection from our own identity.
/// </summary>
internal sealed class SecureSecretFile(Action<SecretFileStage, string>? checkpoint = null)
{
    internal const UnixFileMode FileModeBits = UnixFileMode.UserRead | UnixFileMode.UserWrite;
    internal const UnixFileMode DirectoryModeBits = FileModeBits | UnixFileMode.UserExecute;

    internal string? Read(string path) => Guard(() =>
    {
        if (!EnsureDirectory(Path.GetDirectoryName(path)!, create: false) || !ProtectFile(path))
        {
            return null;
        }
        return File.ReadAllText(path);
    });

    internal bool Exists(string path) => Guard(() =>
        EnsureDirectory(Path.GetDirectoryName(path)!, create: false) && ProtectFile(path));

    internal void Write(string path, string contents) => Guard(() => WriteCore(path, contents, overwrite: true));

    internal string PublishNew(string path, string contents)
    {
        _ = Guard(() => WriteCore(path, contents, overwrite: false));
        // Always use the committed value, including when a competing initializer won.
        return Read(path) ?? throw InvalidInput();
    }

    // A stable lock inode is never replaced/deleted. Cooperating history processes hold the handle
    // throughout key initialization, reads, appends and replacement.
    internal FileStream? TryOpenExclusive(string path) => Guard(() =>
    {
        _ = EnsureDirectory(Path.GetDirectoryName(path)!, create: true);
        try
        {
            if (!ProtectFile(path)) { return Create(path); }
            return new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        }
        catch (IOException) when (File.Exists(path)) { return null; }
    });

    internal void Delete(string path) => Guard(() =>
    {
        if (EnsureDirectory(Path.GetDirectoryName(path)!, create: false) && ProtectFile(path)) { File.Delete(path); }
        return true;
    });

    internal void Append(string path, string ciphertext) => Guard(() =>
    {
        _ = EnsureDirectory(Path.GetDirectoryName(path)!, create: true);
        using var stream = ProtectFile(path)
            ? new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough)
            : Create(path);
        Verify(path, directory: false);
        var bytes = Encoding.UTF8.GetBytes(ciphertext);
        stream.Write(bytes);
        stream.Flush(flushToDisk: true);
        return true;
    });

    // Explicit exports protect the file, without changing the permissions of a user-selected directory.
    internal void Export(string path, string contents) => Guard(() =>
    {
        _ = Attributes(Path.GetDirectoryName(Path.GetFullPath(path))!, directory: true);
        _ = ProtectFile(path);
        using var stream = File.Exists(path)
            ? new FileStream(path, FileMode.Truncate, FileAccess.Write, FileShare.None)
            : Create(path);
        Verify(path, directory: false);
        var bytes = Encoding.UTF8.GetBytes(contents);
        try { stream.Write(bytes); stream.Flush(flushToDisk: true); }
        finally { System.Security.Cryptography.CryptographicOperations.ZeroMemory(bytes); }
        return true;
    });

    internal static SecretStorageException InvalidInput() => new(
        "Secret storage key inputs are missing or invalid. Restore secrets.salt and machine.id from the same backup as secrets.json, then restart Studio. Existing files have been retained.");

    private bool WriteCore(string path, string contents, bool overwrite)
    {
        var directory = Path.GetDirectoryName(path)!;
        _ = EnsureDirectory(directory, create: true);
        _ = ProtectFile(path);
        var temporary = Path.Combine(directory, ".secret-" + Guid.NewGuid().ToString("N") + ".tmp");
        var owned = false;
        try
        {
            checkpoint?.Invoke(SecretFileStage.BeforeCreate, temporary);
            using (var stream = Create(temporary))
            {
                owned = true;
                checkpoint?.Invoke(SecretFileStage.Created, temporary);
                // Verify create-time protection before any bytes are written.
                Verify(temporary, directory: false);
                var bytes = Encoding.UTF8.GetBytes(contents);
                var half = bytes.Length / 2;
                stream.Write(bytes.AsSpan(0, half));
                checkpoint?.Invoke(SecretFileStage.PartialWrite, temporary);
                stream.Write(bytes.AsSpan(half));
                checkpoint?.Invoke(SecretFileStage.BeforeFlush, temporary);
                stream.Flush(flushToDisk: true);
                checkpoint?.Invoke(SecretFileStage.Flushed, temporary);
                checkpoint?.Invoke(SecretFileStage.BeforeClose, temporary);
            }
            checkpoint?.Invoke(SecretFileStage.Closed, temporary);
            checkpoint?.Invoke(SecretFileStage.BeforeReplace, temporary);
            // Recheck targets after writing, before committing. Other identities cannot mutate the
            // protected directory; same-identity hostile races are outside this boundary.
            _ = EnsureDirectory(directory, create: false);
            _ = ProtectFile(path);
            if (!overwrite && !OperatingSystem.IsWindows())
            {
                // File.Move(overwrite: false) on .NET Unix can check for absence then rename over
                // a concurrent winner. link() atomically publishes the complete private inode only
                // if the destination is absent. Keep ownership of the temporary alias for finally.
                if (Link(temporary, path) != 0)
                {
                    var error = Marshal.GetLastPInvokeError();
                    if (error == 17 && ProtectFile(path)) // POSIX EEXIST on Linux and macOS
                    {
                        return false;
                    }
                    // Fail closed if the filesystem cannot publish atomically; never copy into place.
                    throw new Win32Exception(error);
                }
            }
            else
            {
                try
                {
                    File.Move(temporary, path, overwrite);
                }
                catch (IOException) when (!overwrite && ProtectFile(path))
                {
                    return false;
                }
                owned = false;
            }
            checkpoint?.Invoke(SecretFileStage.Replaced, path);
            return true;
        }
        finally
        {
            if (owned)
            {
                // Never sweep arbitrary .tmp files or remove a destination after commit.
                try { File.Delete(temporary); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }
    }

    [DllImport("libc", EntryPoint = "link", SetLastError = true, BestFitMapping = false, ThrowOnUnmappableChar = true)]
    private static extern int Link(
        [MarshalAs(UnmanagedType.LPUTF8Str)] string existing,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string destination);

    private bool EnsureDirectory(string path, bool create)
    {
        var attributes = Attributes(path, directory: true);
        if (attributes is null)
        {
            if (!create)
            {
                return false;
            }
            if (OperatingSystem.IsWindows())
            {
                new DirectoryInfo(path).Create(DirectoryAcl());
            }
            else
            {
                _ = Directory.CreateDirectory(path, DirectoryModeBits);
            }
            checkpoint?.Invoke(SecretFileStage.DirectoryCreated, path);
            _ = Attributes(path, directory: true);
        }
        checkpoint?.Invoke(SecretFileStage.BeforeDirectoryProtection, path);
        if (OperatingSystem.IsWindows())
        {
            // DirectoryInfo.SetAccessControl uses SetNamedSecurityInfo, which recursively
            // recalculates inherited ACLs on existing children. This shared directory also holds
            // unrelated files. Apply only this object's DACL; creation/ACL construction/verification
            // still use the .NET reference-pack APIs. No new dependency is needed.
            if (!SetFileSecurity(path, 0x80000004, DirectoryAcl().GetSecurityDescriptorBinaryForm()))
            {
                throw new Win32Exception(Marshal.GetLastPInvokeError());
            }
        }
        else
        {
            File.SetUnixFileMode(path, DirectoryModeBits);
        }
        Verify(path, directory: true);
        return true;
    }

    private bool ProtectFile(string path)
    {
        if (Attributes(path, directory: false) is null)
        {
            return false;
        }
        checkpoint?.Invoke(SecretFileStage.BeforeFileProtection, path);
        if (OperatingSystem.IsWindows())
        {
            new FileInfo(path).SetAccessControl(FileAcl());
        }
        else
        {
            File.SetUnixFileMode(path, FileModeBits);
        }
        Verify(path, directory: false);
        return true;
    }

    private static FileAttributes? Attributes(string path, bool directory)
    {
        FileAttributes attributes;
        try { attributes = File.GetAttributes(path); }
        catch (FileNotFoundException) { return null; }
        catch (DirectoryNotFoundException) { return null; }
        if ((attributes & FileAttributes.ReparsePoint) != 0)
        {
            throw new SecretStorageException(
                "Secret storage refuses linked directories or files. Restore regular files in the Studio application directory and retry. See Studio troubleshooting.");
        }
        if (((attributes & FileAttributes.Directory) != 0) != directory)
        {
            throw new IOException();
        }
        return attributes;
    }

    private static FileStream Create(string path)
    {
        if (OperatingSystem.IsWindows())
        {
            return new FileInfo(path).Create(FileMode.CreateNew, FileSystemRights.FullControl,
                FileShare.None, 4096, FileOptions.WriteThrough, FileAcl());
        }
        return new FileStream(path, new FileStreamOptions
        {
            Mode = FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.None,
            Options = FileOptions.WriteThrough, UnixCreateMode = FileModeBits
        });
    }

    private static void Verify(string path, bool directory)
    {
        if (OperatingSystem.IsWindows())
        {
            VerifyAcl(path, directory);
        }
        else if (File.GetUnixFileMode(path) != (directory ? DirectoryModeBits : FileModeBits))
        {
            throw new UnauthorizedAccessException();
        }
    }

    // DACL_SECURITY_INFORMATION | PROTECTED_DACL_SECURITY_INFORMATION. The deliberately local
    // semantics of this API are documented at https://learn.microsoft.com/windows/win32/api/securitybaseapi/nf-securitybaseapi-setfilesecurityw .
    [DllImport("advapi32.dll", EntryPoint = "SetFileSecurityW", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetFileSecurity(string path, uint securityInformation, byte[] securityDescriptor);

    [SupportedOSPlatform("windows")]
    private static SecurityIdentifier[] Principals()
    {
        using var identity = WindowsIdentity.GetCurrent();
        var user = identity.User ?? throw new UnauthorizedAccessException();
        var system = new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null);
        return user == system ? [user] : [user, system];
    }

    [SupportedOSPlatform("windows")]
    private static DirectorySecurity DirectoryAcl()
    {
        var acl = new DirectorySecurity();
        SetRules(acl, directory: true);
        return acl;
    }

    [SupportedOSPlatform("windows")]
    private static FileSecurity FileAcl()
    {
        var acl = new FileSecurity();
        SetRules(acl, directory: false);
        return acl;
    }

    [SupportedOSPlatform("windows")]
    private static void SetRules(FileSystemSecurity acl, bool directory)
    {
        acl.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        foreach (var principal in Principals())
        {
            acl.AddAccessRule(new FileSystemAccessRule(principal, FileSystemRights.FullControl,
                directory ? InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit : InheritanceFlags.None,
                PropagationFlags.None, AccessControlType.Allow));
        }
    }

    [SupportedOSPlatform("windows")]
    private static void VerifyAcl(string path, bool directory)
    {
        FileSystemSecurity acl = directory
            ? new DirectoryInfo(path).GetAccessControl(AccessControlSections.Access)
            : new FileInfo(path).GetAccessControl(AccessControlSections.Access);
        var principals = Principals();
        var rules = acl.GetAccessRules(true, true, typeof(SecurityIdentifier)).Cast<FileSystemAccessRule>().ToArray();
        if (!acl.AreAccessRulesProtected || rules.Length != principals.Length
            || principals.Any(principal => !rules.Any(rule => rule.IdentityReference == principal))
            || rules.Any(rule => rule.IsInherited || rule.AccessControlType != AccessControlType.Allow
                || rule.FileSystemRights != FileSystemRights.FullControl
                || rule.PropagationFlags != PropagationFlags.None
                || rule.InheritanceFlags != (directory
                    ? InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit : InheritanceFlags.None)))
        {
            throw new UnauthorizedAccessException();
        }
    }

    private static T Guard<T>(Func<T> operation)
    {
        try { return operation(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException
            or Win32Exception or NotSupportedException or ArgumentException)
        {
            // Raw OS error messages and paths can contain credentials or user-controlled text.
            throw new SecretStorageException(
                "Secret storage could not protect or access its files. Check ownership, permissions, free space, and filesystem support for private files in the Studio application directory, then retry. See Studio troubleshooting.");
        }
    }
}
