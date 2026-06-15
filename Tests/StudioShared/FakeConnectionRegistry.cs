using GrpCurl.Net.Studio.ViewModels.Models.Connections;
using GrpCurl.Net.Studio.ViewModels.Services;

namespace GrpCurl.Net.Studio.TestSupport;

/// <summary>Scripted <see cref="IConnectionRegistry" /> returning a fixed probe result.</summary>
public sealed class FakeConnectionRegistry : IConnectionRegistry
{
    public TestConnectionResult Result { get; set; } = TestConnectionResult.Success(1);

    public SavedConnection? LastTested { get; private set; }

    public Task<TestConnectionResult> TestConnectionAsync(SavedConnection connection, CancellationToken cancellationToken = default)
    {
        LastTested = connection;
        return Task.FromResult(Result);
    }
}
