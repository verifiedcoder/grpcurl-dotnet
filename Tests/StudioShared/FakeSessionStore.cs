using GrpCurl.Net.Studio.ViewModels.Models.Session;
using GrpCurl.Net.Studio.ViewModels.Services;

namespace GrpCurl.Net.Studio.TestSupport;

/// <summary>In-memory <see cref="ISessionStore" />: <see cref="State" /> is returned by Load; saves land in both.</summary>
public sealed class FakeSessionStore : ISessionStore
{
    public SessionState State { get; set; } = new();

    public SessionState? LastSaved { get; private set; }

    public int SaveCount { get; private set; }

    public Task<SessionState> LoadAsync(CancellationToken cancellationToken = default) => Task.FromResult(State);

    public Task SaveAsync(SessionState state, CancellationToken cancellationToken = default)
    {
        LastSaved = state;
        State = state;
        SaveCount++;
        return Task.CompletedTask;
    }
}
