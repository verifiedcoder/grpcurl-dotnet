using GrpCurl.Net.Studio.ViewModels.Models;

namespace GrpCurl.Net.Studio.ViewModels.Services;

/// <summary>
///     Loads and persists <see cref="StudioSettings" /> (SPEC-040). <see cref="Current" /> is
///     the last-loaded/saved snapshot, available synchronously for binding; mutations are
///     written via <see cref="SaveAsync" />.
/// </summary>
public interface ISettingsStore
{
    StudioSettings Current { get; }

    Task<StudioSettings> LoadAsync(CancellationToken cancellationToken = default);

    Task SaveAsync(StudioSettings settings, CancellationToken cancellationToken = default);
}
