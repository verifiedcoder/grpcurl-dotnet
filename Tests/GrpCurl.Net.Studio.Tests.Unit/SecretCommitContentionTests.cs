using GrpCurl.Net.Studio.Services.Secrets;

namespace GrpCurl.Net.Studio.Tests.Unit;

public sealed class SecretCommitContentionTests
{
    [Theory]
    [InlineData(5)]
    [InlineData(32)]
    [InlineData(33)]
    public void Windows_commit_contention_retries_the_same_complete_file(int code)
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Windows replacement contention only.");
        var directory = Path.Combine(Path.GetTempPath(), "grpcn-commit-" + Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "secret.json");
        var writes = 0;
        var attempts = 0;
        var files = new SecureSecretFile((stage, _) =>
        {
            if (stage == SecretFileStage.PartialWrite) { writes++; }
            if (stage == SecretFileStage.BeforeCommitAttempt && ++attempts <= 2)
            {
                if (code == 5) { throw new UnauthorizedAccessException("private-error-canary"); }
                throw new IOException("private-error-canary", unchecked((int)0x80070000) | code);
            }
        });
        try
        {
            files.Write(path, "complete");
            files.Read(path).ShouldBe("complete");
            writes.ShouldBe(1);
            attempts.ShouldBe(3);
            Directory.GetFiles(directory, ".secret-*.tmp").ShouldBeEmpty();
        }
        finally { if (Directory.Exists(directory)) { Directory.Delete(directory, true); } }
    }

    [Theory]
    [InlineData(5, 10)]
    [InlineData(32, 10)]
    [InlineData(112, 1)] // disk full is not a transient sharing condition
    public void Permanent_commit_failure_preserves_previous_data_and_removes_owned_temporary(int code, int expectedAttempts)
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Windows replacement contention only.");
        var directory = Path.Combine(Path.GetTempPath(), "grpcn-commit-" + Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "secret.json");
        var attempts = 0;
        try
        {
            new SecureSecretFile().Write(path, "previous");
            var files = new SecureSecretFile((stage, _) =>
            {
                if (stage == SecretFileStage.BeforeCommitAttempt)
                {
                    attempts++;
                    if (code == 5) { throw new UnauthorizedAccessException("private-error-canary"); }
                    throw new IOException("private-error-canary", unchecked((int)0x80070000) | code);
                }
            });
            var error = Should.Throw<SecretStorageException>(() => files.Write(path, "replacement"));
            error.ToString().ShouldNotContain("private-error-canary");
            File.ReadAllText(path).ShouldBe("previous");
            attempts.ShouldBe(expectedAttempts);
            Directory.GetFiles(directory, ".secret-*.tmp").ShouldBeEmpty();
        }
        finally { if (Directory.Exists(directory)) { Directory.Delete(directory, true); } }
    }
}
