using GrpCurl.Net.Studio.Services.Secrets;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;

namespace GrpCurl.Net.Studio.Tests.Unit;

public sealed class SecureSecretFileTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "grpcn-secure-" + Guid.NewGuid().ToString("N"));
    private string Target => Path.Combine(_root, "studio", "secrets.json");

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Theory]
    [InlineData("BeforeCreate")]
    [InlineData("Created")]
    [InlineData("PartialWrite")]
    [InlineData("BeforeFlush")]
    [InlineData("Flushed")]
    [InlineData("BeforeClose")]
    [InlineData("Closed")]
    [InlineData("BeforeReplace")]
    [InlineData("Replaced")]
    public void Failed_write_preserves_old_contents_until_commit_and_complete_new_contents_after(string failure)
    {
        new SecureSecretFile().Write(Target, "old valid contents");
        var abandoned = Path.Combine(Path.GetDirectoryName(Target)!, ".secret-abandoned.tmp");
        File.WriteAllText(abandoned, "abandoned");
        var next = new string('N', 20000);
        var files = new SecureSecretFile((stage, _) =>
        {
            File.ReadAllText(Target).ShouldBe(stage == SecretFileStage.Replaced ? next : "old valid contents");
            if (stage.ToString() == failure)
            {
                throw new IOException("sensitive-os-error-canary");
            }
        });
        var error = Should.Throw<SecretStorageException>(() => files.Write(Target, next));
        error.ToString().ShouldNotContain("sensitive-os-error-canary");
        error.InnerException.ShouldBeNull();
        error.Message.ShouldContain("permissions");
        new SecureSecretFile().Read(Target).ShouldBe(failure == "Replaced" ? next : "old valid contents");
        Directory.GetFiles(Path.GetDirectoryName(Target)!, "*.tmp").ShouldBe([abandoned]);
    }

    [Fact]
    public void Directory_and_temporary_file_are_private_at_creation_and_after_replacement()
    {
        var observed = new List<SecretFileStage>();
        var files = new SecureSecretFile((stage, path) =>
        {
            if (stage is SecretFileStage.DirectoryCreated or SecretFileStage.Created)
            {
                AssertPrivate(path, stage == SecretFileStage.DirectoryCreated);
                observed.Add(stage);
            }
        });
        files.Write(Target, "complete");
        observed.ShouldBe([SecretFileStage.DirectoryCreated, SecretFileStage.Created]);
        AssertPrivate(Target, false);
        files.Read(Target).ShouldBe("complete");
    }

    [Theory]
    [InlineData("BeforeDirectoryProtection")]
    [InlineData("BeforeFileProtection")]
    public void Protection_failure_prevents_read_or_write_and_is_sanitized(string failure)
    {
        new SecureSecretFile().Write(Target, "old");
        var files = new SecureSecretFile((stage, _) =>
        {
            if (stage.ToString() == failure)
            {
                throw new UnauthorizedAccessException("credential-in-error");
            }
        });
        _ = Should.Throw<SecretStorageException>(() => files.Read(Target));
        var error = Should.Throw<SecretStorageException>(() => files.Write(Target, "new"));
        error.ToString().ShouldNotContain("credential-in-error");
        File.ReadAllText(Target).ShouldBe("old");
        Directory.GetFiles(Path.GetDirectoryName(Target)!, "*.tmp").ShouldBeEmpty();
    }

    [Fact]
    public void Existing_permissions_are_tightened_without_touching_unrelated_files()
    {
        var directory = Path.GetDirectoryName(Target)!;
        _ = Directory.CreateDirectory(directory);
        File.WriteAllText(Target, "old");
        var unrelated = Path.Combine(directory, "settings.json");
        File.WriteAllText(unrelated, "settings");
        if (OperatingSystem.IsWindows())
        {
            MakeBroadAcl(directory, true);
            MakeBroadAcl(Target, false);
            var before = new FileInfo(unrelated).GetAccessControl().GetSecurityDescriptorSddlForm(AccessControlSections.Access);
            new SecureSecretFile().Read(Target).ShouldBe("old");
            new FileInfo(unrelated).GetAccessControl().GetSecurityDescriptorSddlForm(AccessControlSections.Access).ShouldBe(before);
        }
        else
        {
            var broad = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
                | UnixFileMode.GroupRead | UnixFileMode.GroupWrite | UnixFileMode.GroupExecute
                | UnixFileMode.OtherRead | UnixFileMode.OtherWrite | UnixFileMode.OtherExecute;
            File.SetUnixFileMode(directory, broad);
            File.SetUnixFileMode(Target, broad);
            var before = File.GetUnixFileMode(unrelated);
            new SecureSecretFile().Read(Target).ShouldBe("old");
            File.GetUnixFileMode(unrelated).ShouldBe(before);
        }
        AssertPrivate(directory, true);
        AssertPrivate(Target, false);
    }

    [Fact]
    public void Failure_to_retain_private_create_permissions_is_detected_before_writing()
    {
        var files = new SecureSecretFile((stage, path) =>
        {
            if (stage == SecretFileStage.Created)
            {
                if (OperatingSystem.IsWindows()) { MakeBroadAcl(path, false); }
                else { File.SetUnixFileMode(path, (UnixFileMode)438); }
            }
        });
        _ = Should.Throw<SecretStorageException>(() => files.Write(Target, "must-not-persist"));
        File.Exists(Target).ShouldBeFalse();
        Directory.GetFiles(Path.GetDirectoryName(Target)!, "*.tmp").ShouldBeEmpty();
    }

    [Fact]
    public void Exclusive_creation_never_deletes_a_file_it_did_not_create()
    {
        string? preexisting = null;
        var files = new SecureSecretFile((stage, path) =>
        {
            if (stage == SecretFileStage.BeforeCreate)
            {
                File.WriteAllText(path, "not ours");
                preexisting = path;
            }
        });
        _ = Should.Throw<SecretStorageException>(() => files.Write(Target, "secret"));
        File.ReadAllText(preexisting!).ShouldBe("not ours");
        File.Exists(Target).ShouldBeFalse();
    }

    [Fact]
    public async Task Competing_publishers_both_load_the_winning_complete_input()
    {
        using var ready = new Barrier(2);
        var files = new SecureSecretFile((stage, _) =>
        {
            if (stage == SecretFileStage.BeforeReplace)
            {
                ready.SignalAndWait(TimeSpan.FromSeconds(10)).ShouldBeTrue();
            }
        });
        var ct = TestContext.Current.CancellationToken;
        var results = await Task.WhenAll(
            Task.Run(() => files.PublishNew(Target, "first"), ct),
            Task.Run(() => files.PublishNew(Target, "second"), ct));
        results[0].ShouldBe(results[1]);
        File.ReadAllText(Target).ShouldBe(results[0]);
        Directory.GetFiles(Path.GetDirectoryName(Target)!, "*.tmp").ShouldBeEmpty();
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, false)]
    [InlineData(false, true)]
    public void Linked_directories_files_and_dangling_links_are_rejected_before_hardening(bool directory, bool dangling)
    {
        _ = Directory.CreateDirectory(_root);
        var external = Path.Combine(_root, "external");
        var target = directory ? Path.GetDirectoryName(Target)! : Target;
        if (directory)
        {
            _ = Directory.CreateDirectory(external);
            File.WriteAllText(Path.Combine(external, "secrets.json"), "untouched");
        }
        else
        {
            _ = Directory.CreateDirectory(Path.GetDirectoryName(Target)!);
            if (!dangling) { File.WriteAllText(external, "untouched"); }
        }
        // CI Windows runners have symlink privilege. A failure here is a test failure, not an
        // unreported skip; the three-OS matrix must exercise this boundary.
        if (directory) { _ = Directory.CreateSymbolicLink(target, external); }
        else { _ = File.CreateSymbolicLink(target, external); }
        var protectedPaths = new List<string>();
        var files = new SecureSecretFile((stage, path) =>
        {
            if (stage is SecretFileStage.BeforeDirectoryProtection or SecretFileStage.BeforeFileProtection)
            {
                protectedPaths.Add(path);
            }
        });
        try
        {
            _ = Should.Throw<SecretStorageException>(() => files.Read(Target));
            _ = Should.Throw<SecretStorageException>(() => files.Write(Target, "new"));
            protectedPaths.ShouldNotContain(target);
            if (!dangling)
            {
                File.ReadAllText(directory ? Path.Combine(external, "secrets.json") : external).ShouldBe("untouched");
            }
        }
        finally
        {
            if (directory) { Directory.Delete(target); }
            else { File.Delete(target); }
        }
    }

    internal static void AssertPrivate(string path, bool directory)
    {
        if (!OperatingSystem.IsWindows())
        {
            File.GetUnixFileMode(path).ShouldBe(directory ? SecureSecretFile.DirectoryModeBits : SecureSecretFile.FileModeBits);
            return;
        }
        FileSystemSecurity acl = directory ? new DirectoryInfo(path).GetAccessControl() : new FileInfo(path).GetAccessControl();
        acl.AreAccessRulesProtected.ShouldBeTrue();
        using var identity = WindowsIdentity.GetCurrent();
        var expected = new[] { identity.User!, new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null) }.Distinct().ToArray();
        var rules = acl.GetAccessRules(true, true, typeof(SecurityIdentifier)).Cast<FileSystemAccessRule>().ToArray();
        rules.Select(rule => rule.IdentityReference).ShouldBe(expected, ignoreOrder: true);
        foreach (var rule in rules)
        {
            rule.IsInherited.ShouldBeFalse();
            rule.AccessControlType.ShouldBe(AccessControlType.Allow);
            rule.FileSystemRights.ShouldBe(FileSystemRights.FullControl);
            rule.InheritanceFlags.ShouldBe(directory
                ? InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit : InheritanceFlags.None);
        }
    }

    [SupportedOSPlatform("windows")]
    private static void MakeBroadAcl(string path, bool directory)
    {
        FileSystemSecurity acl = directory ? new DirectoryInfo(path).GetAccessControl() : new FileInfo(path).GetAccessControl();
        acl.SetAccessRuleProtection(false, true);
        acl.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.WorldSid, null),
            FileSystemRights.Read, AccessControlType.Allow));
        if (directory) { new DirectoryInfo(path).SetAccessControl((DirectorySecurity)acl); }
        else { new FileInfo(path).SetAccessControl((FileSecurity)acl); }
    }
}
