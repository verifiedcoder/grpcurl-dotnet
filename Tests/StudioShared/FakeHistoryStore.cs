using GrpCurl.Net.Studio.ViewModels.Models.History;
using GrpCurl.Net.Studio.ViewModels.Services;

namespace GrpCurl.Net.Studio.TestSupport;

/// <summary>In-memory <see cref="IHistoryStore" /> recording appends, for recorder/UI tests.</summary>
public sealed class FakeHistoryStore : IHistoryStore
{
    public List<HistoryEntry> Entries { get; } = [];

    public HistoryEntry? Last => Entries.Count == 0 ? null : Entries[^1];

    public Task AppendAsync(HistoryEntry entry, CancellationToken cancellationToken = default)
    {
        Entries.Add(entry);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<HistoryEntry>> ReadAllAsync(CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<HistoryEntry>>(Entries.ToList());

    public Task SetPinnedAsync(string id, bool pinned, CancellationToken cancellationToken = default)
    {
        for (var i = 0; i < Entries.Count; i++)
        {
            if (Entries[i].Id == id)
            {
                Entries[i] = Entries[i] with { Pinned = pinned };
            }
        }

        return Task.CompletedTask;
    }

    public Task DeleteAsync(IReadOnlyCollection<string> ids, CancellationToken cancellationToken = default)
    {
        Entries.RemoveAll(e => ids.Contains(e.Id));
        return Task.CompletedTask;
    }

    public Task ClearAsync(bool keepPinned = false, CancellationToken cancellationToken = default)
    {
        Entries.RemoveAll(e => !keepPinned || !e.Pinned);
        return Task.CompletedTask;
    }

    public string? ExportedPath { get; private set; }

    public IReadOnlyList<HistoryEntry>? ExportedEntries { get; private set; }

    public Task ExportAsync(string path, IReadOnlyList<HistoryEntry> entries, CancellationToken cancellationToken = default)
    {
        ExportedPath = path;
        ExportedEntries = entries;
        return Task.CompletedTask;
    }
}
