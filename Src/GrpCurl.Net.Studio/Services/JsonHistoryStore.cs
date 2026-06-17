using System.Text;
using System.Text.Json;
using GrpCurl.Net.Studio.ViewModels.Models.History;
using GrpCurl.Net.Studio.ViewModels.Services;

namespace GrpCurl.Net.Studio.Services;

/// <summary>
///     NDJSON-backed <see cref="IHistoryStore" /> (SPEC-040 §5, ADR-008). Each entry is one UTF-8 line,
///     appended with a single write + flush. The file is the only source of truth (no binary index in v1).
///     Retention evicts oldest-unpinned-first once the entry or byte cap is exceeded; eviction, pin toggles,
///     and deletes rewrite via the atomic temp-and-rename pattern. Reads tolerate a truncated tail line.
/// </summary>
internal sealed class JsonHistoryStore : IHistoryStore
{
    private const string AppFolderName = "GrpCurlNet.Studio";
    private const string FileName = "history.ndjson";

    private readonly string _path;
    private readonly int _maxEntries;
    private readonly long _maxBytes;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public JsonHistoryStore()
        : this(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            AppFolderName,
            FileName))
    {
    }

    // Test seam: point at a temp file with (optionally) tighter caps to exercise retention.
    internal JsonHistoryStore(string path, int maxEntries = 1000, long maxBytes = 50L * 1024 * 1024)
    {
        _path = path;
        _maxEntries = Math.Max(1, maxEntries);
        _maxBytes = Math.Max(1, maxBytes);
    }

    public async Task AppendAsync(HistoryEntry entry, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            await File.AppendAllTextAsync(_path, Serialize(entry) + "\n", Utf8NoBom, cancellationToken).ConfigureAwait(false);

            await EnforceRetentionAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<IReadOnlyList<HistoryEntry>> ReadAllAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            return ReadAllUnlocked();
        }
        finally
        {
            _gate.Release();
        }
    }

    public Task SetPinnedAsync(string id, bool pinned, CancellationToken cancellationToken = default)
        => MutateAsync(entries => entries.Select(e => e.Id == id ? e with { Pinned = pinned } : e).ToList(), cancellationToken);

    public Task DeleteAsync(IReadOnlyCollection<string> ids, CancellationToken cancellationToken = default)
    {
        var drop = ids as HashSet<string> ?? [.. ids];
        return MutateAsync(entries => entries.Where(e => !drop.Contains(e.Id)).ToList(), cancellationToken);
    }

    public Task ClearAsync(bool keepPinned = false, CancellationToken cancellationToken = default)
        => MutateAsync(entries => keepPinned ? entries.Where(e => e.Pinned).ToList() : [], cancellationToken);

    public async Task ExportAsync(string path, IReadOnlyList<HistoryEntry> entries, CancellationToken cancellationToken = default)
    {
        var builder = new StringBuilder();

        foreach (var entry in entries)
        {
            builder.Append(Serialize(entry)).Append('\n');
        }

        await File.WriteAllTextAsync(path, builder.ToString(), Utf8NoBom, cancellationToken).ConfigureAwait(false);
    }

    private async Task MutateAsync(Func<List<HistoryEntry>, List<HistoryEntry>> transform, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            await RewriteAsync(transform([.. ReadAllUnlocked()]), cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task EnforceRetentionAsync(CancellationToken cancellationToken)
    {
        var entries = ReadAllUnlocked();
        var lines = entries.Select(Serialize).ToList();
        var totalBytes = lines.Sum(l => (long)Utf8NoBom.GetByteCount(l) + 1); // + newline

        if (entries.Count <= _maxEntries && totalBytes <= _maxBytes)
        {
            return;
        }

        // Evict oldest-unpinned-first until both caps are satisfied (or only pinned entries remain).
        var kept = new List<HistoryEntry>(entries);
        var keptBytes = totalBytes;

        while ((kept.Count > _maxEntries || keptBytes > _maxBytes)
               && kept.FindIndex(e => !e.Pinned) is var index and >= 0)
        {
            keptBytes -= Utf8NoBom.GetByteCount(Serialize(kept[index])) + 1;
            kept.RemoveAt(index);
        }

        if (kept.Count != entries.Count)
        {
            await RewriteAsync(kept, cancellationToken).ConfigureAwait(false);
        }
    }

    private List<HistoryEntry> ReadAllUnlocked()
    {
        if (!File.Exists(_path))
        {
            return [];
        }

        var entries = new List<HistoryEntry>();

        foreach (var line in File.ReadLines(_path, Utf8NoBom))
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            HistoryEntry? entry;

            try
            {
                entry = JsonSerializer.Deserialize(line, HistoryJsonContext.Default.HistoryEntry);
            }
            catch (JsonException)
            {
                break; // a truncated tail line — stop and keep everything parsed so far
            }

            if (entry is not null)
            {
                entries.Add(entry);
            }
        }

        return entries;
    }

    private async Task RewriteAsync(IReadOnlyList<HistoryEntry> entries, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);

        if (entries.Count == 0)
        {
            File.Delete(_path);
            return;
        }

        var builder = new StringBuilder();

        foreach (var entry in entries)
        {
            builder.Append(Serialize(entry)).Append('\n');
        }

        var tempPath = _path + ".tmp-" + Guid.NewGuid().ToString("N");
        await File.WriteAllTextAsync(tempPath, builder.ToString(), Utf8NoBom, cancellationToken).ConfigureAwait(false);
        File.Move(tempPath, _path, overwrite: true);
    }

    private static string Serialize(HistoryEntry entry)
        => JsonSerializer.Serialize(entry, HistoryJsonContext.Default.HistoryEntry);

    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);
}
