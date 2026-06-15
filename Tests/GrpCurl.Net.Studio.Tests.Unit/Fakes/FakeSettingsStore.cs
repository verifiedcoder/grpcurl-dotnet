using GrpCurl.Net.Studio.ViewModels.Models;
using GrpCurl.Net.Studio.ViewModels.Services;

namespace GrpCurl.Net.Studio.Tests.Unit.Fakes;

/// <summary>In-memory <see cref="ISettingsStore" /> that records the last saved settings.</summary>
public sealed class FakeSettingsStore : ISettingsStore
{
    public event EventHandler? Changed;

    public StudioSettings Current { get; private set; } = StudioSettings.Defaults();

    public int SaveCount { get; private set; }

    public Task<StudioSettings> LoadAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(Current);

    public Task SaveAsync(StudioSettings settings, CancellationToken cancellationToken = default)
    {
        Current = settings;
        SaveCount++;
        Changed?.Invoke(this, EventArgs.Empty);
        return Task.CompletedTask;
    }
}
