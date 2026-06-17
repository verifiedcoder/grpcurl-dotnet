using GrpCurl.Net.Studio.ViewModels.Models;
using GrpCurl.Net.Studio.ViewModels.Services;

namespace GrpCurl.Net.Studio.Services;

/// <summary>
///     An in-memory <see cref="ISettingsStore" /> that serves defaults and keeps saved settings only for
///     the process lifetime. The app uses the JSON-backed <see cref="JsonSettingsStore" />; this remains
///     as a lightweight test double (exposed to the test projects via InternalsVisibleTo).
/// </summary>
internal sealed class InMemorySettingsStore : ISettingsStore
{
    public event EventHandler? Changed;

    public StudioSettings Current { get; private set; } = StudioSettings.Defaults();

    public Task<StudioSettings> LoadAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(Current);

    public Task SaveAsync(StudioSettings settings, CancellationToken cancellationToken = default)
    {
        Current = settings;
        Changed?.Invoke(this, EventArgs.Empty);
        return Task.CompletedTask;
    }
}
