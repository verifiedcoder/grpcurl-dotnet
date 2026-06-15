using GrpCurl.Net.Studio.ViewModels.Models.Connections;
using GrpCurl.Net.Studio.ViewModels.Models.Descriptors;
using GrpCurl.Net.Studio.ViewModels.Services;

namespace GrpCurl.Net.Studio.TestSupport;

/// <summary>Scripted <see cref="IDescriptorService" />: returns a canned result or a custom handler.</summary>
public sealed class FakeDescriptorService : IDescriptorService
{
    /// <summary>Result returned when <see cref="OnLoad" /> is not set.</summary>
    public DescriptorLoadResult Result { get; set; } = DescriptorLoadResult.Success(ServiceCatalog.Empty);

    /// <summary>Optional custom behaviour (e.g. to honour cancellation or throw).</summary>
    public Func<SavedConnection, CancellationToken, Task<DescriptorLoadResult>>? OnLoad { get; set; }

    public SavedConnection? LastLoaded { get; private set; }

    public int LoadCount { get; private set; }

    public Task<DescriptorLoadResult> LoadAsync(SavedConnection connection, CancellationToken cancellationToken = default)
    {
        LastLoaded = connection;
        LoadCount++;

        if (OnLoad is not null)
        {
            return OnLoad(connection, cancellationToken);
        }

        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(Result);
    }
}
