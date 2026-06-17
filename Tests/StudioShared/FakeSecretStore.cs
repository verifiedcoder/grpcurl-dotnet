using GrpCurl.Net.Studio.ViewModels.Services;

namespace GrpCurl.Net.Studio.TestSupport;

/// <summary>In-memory <see cref="ISecretStore" /> backed by a dictionary, for service-layer tests.</summary>
public sealed class FakeSecretStore : ISecretStore
{
    private readonly Dictionary<string, string> _values = [];

    /// <summary>Overridable backend descriptor so tests can simulate the fallback (SEC-024) surface.</summary>
    public SecretStoreInfo Info { get; set; } = new("In-memory (test)", IsOsKeychain: false, LimitationNote: null);

    public Task SetAsync(string keyRef, string value, CancellationToken cancellationToken = default)
    {
        _values[keyRef] = value;
        return Task.CompletedTask;
    }

    public Task<string?> GetAsync(string keyRef, CancellationToken cancellationToken = default)
        => Task.FromResult(_values.TryGetValue(keyRef, out var value) ? value : null);

    public Task DeleteAsync(string keyRef, CancellationToken cancellationToken = default)
    {
        _values.Remove(keyRef);
        return Task.CompletedTask;
    }

    public Task<bool> ExistsAsync(string keyRef, CancellationToken cancellationToken = default)
        => Task.FromResult(_values.ContainsKey(keyRef));
}
