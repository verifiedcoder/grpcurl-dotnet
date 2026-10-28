using GrpCurl.Net.Studio.Services.Secrets;
using GrpCurl.Net.Studio.ViewModels.Models;
using GrpCurl.Net.Studio.ViewModels.Services;

namespace GrpCurl.Net.Studio.Tests.Unit;

// These tests use only APIs present at c05d8cf so they can also demonstrate the baseline failures.
public sealed class SecretPersistenceRegressionTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "grpcn-prd013-" + Guid.NewGuid().ToString("N"));
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose()
    {
        if (Directory.Exists(_directory)) { Directory.Delete(_directory, true); }
    }

    [Fact]
    public async Task Existing_directory_and_ciphertext_are_private_after_read()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Unix mode inspection only.");
        using var store = new EncryptedFileSecretStore(_directory);
        await store.SetAsync("key", "secret", Ct);
        File.SetUnixFileMode(_directory, (UnixFileMode)511);
        File.SetUnixFileMode(Path.Combine(_directory, "secrets.json"), (UnixFileMode)438);
        (await store.GetAsync("key", Ct)).ShouldBe("secret");
        File.GetUnixFileMode(_directory).ShouldBe((UnixFileMode)448);
        File.GetUnixFileMode(Path.Combine(_directory, "secrets.json")).ShouldBe((UnixFileMode)384);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("corrupt")]
    [InlineData("AA==")]
    public async Task Missing_or_malformed_salt_with_ciphertext_is_reported_without_regeneration(string? replacement)
    {
        using (var original = new EncryptedFileSecretStore(_directory))
        {
            await original.SetAsync("key", "secret", Ct);
        }
        var salt = Path.Combine(_directory, "secrets.salt");
        var ciphertext = File.ReadAllText(Path.Combine(_directory, "secrets.json"));
        if (replacement is null) { File.Delete(salt); }
        else { File.WriteAllText(salt, replacement); }
        using var restarted = new EncryptedFileSecretStore(_directory);
        _ = await Should.ThrowAsync<SecretBackendUnavailableException>(() => restarted.GetAsync("key", Ct));
        File.ReadAllText(Path.Combine(_directory, "secrets.json")).ShouldBe(ciphertext);
        if (replacement is null) { File.Exists(salt).ShouldBeFalse(); }
        else { File.ReadAllText(salt).ShouldBe(replacement); }
    }

    [Fact]
    public async Task Index_access_failure_is_not_treated_as_an_empty_list()
    {
        _ = Directory.CreateDirectory(Path.Combine(_directory, "secret-keyrefs.json"));
        using var store = new SecretStore(_directory, null, new EncryptedFileSecretStore(_directory), new(SecretFallbackMode.Allow));
        _ = await Should.ThrowAsync<SecretBackendUnavailableException>(() => store.ListAsync(Ct));
    }

    [Fact]
    public async Task Index_is_private_after_native_or_fallback_write()
    {
        using var store = new SecretStore(_directory, null, new EncryptedFileSecretStore(_directory), new(SecretFallbackMode.Allow));
        await store.SetAsync("key", "secret", Ct);
        if (!OperatingSystem.IsWindows())
        {
            File.GetUnixFileMode(Path.Combine(_directory, "secret-keyrefs.json")).ShouldBe((UnixFileMode)384);
        }
    }
}
