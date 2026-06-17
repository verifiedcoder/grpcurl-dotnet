using GrpCurl.Net.Studio.ViewModels.Models.History;

namespace GrpCurl.Net.Studio.ViewModels.Services;

/// <summary>
///     Append-only history of invocations (SPEC-040 §5, ADR-008). Entries arrive already redacted
///     (FR-121) and are persisted one-per-line to <c>history.ndjson</c>; the file is the single source of
///     truth. Retention evicts oldest-unpinned-first once the entry/byte caps are exceeded (FR-126).
/// </summary>
public interface IHistoryStore
{
    /// <summary>Appends one entry and enforces the retention caps. A no-op when capture is disabled upstream.</summary>
    Task AppendAsync(HistoryEntry entry, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Reads every entry in file order (oldest first), tolerantly: a truncated final line is dropped
    ///     rather than failing the read (SPEC-040 §1 history-corruption rule).
    /// </summary>
    Task<IReadOnlyList<HistoryEntry>> ReadAllAsync(CancellationToken cancellationToken = default);

    /// <summary>Pins or unpins an entry; pinned entries are exempt from retention eviction (FR-124).</summary>
    Task SetPinnedAsync(string id, bool pinned, CancellationToken cancellationToken = default);

    /// <summary>Deletes the given entries by id (immediate, irreversible; FR-125).</summary>
    Task DeleteAsync(IReadOnlyCollection<string> ids, CancellationToken cancellationToken = default);

    /// <summary>Clears entries: all of them, or only the unpinned ones (FR-125).</summary>
    Task ClearAsync(bool keepPinned = false, CancellationToken cancellationToken = default);
}
