using GrpCurl.Net.Studio.Services.Secrets;
using GrpCurl.Net.Studio.ViewModels.Models;
using GrpCurl.Net.Studio.ViewModels.Models.History;
using GrpCurl.Net.Studio.ViewModels.Services;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace GrpCurl.Net.Studio.Services;

/// <summary>Private encrypted NDJSON. The stable process lock covers key initialization and every file operation.</summary>
internal sealed class JsonHistoryStore : IHistoryStore, IHistoryStatus, IDisposable
{
    private readonly string _path;
    private readonly ISettingsStore? _settings;
    private readonly HistorySettings _defaults;
    private readonly IHistoryPolicy _policy;
    private readonly SecureSecretFile _files;
    private readonly HistoryCipher _cipher;
    private readonly SemaphoreSlim _gate = new(1, 1); // Never dispose under admitted waiters (PRD-005).
    private readonly TimeSpan _lockTimeout;
    private int _disposed;
    private List<HistoryEntry>? _cachedEntries;
    private string? _cachedCiphertext;

    public JsonHistoryStore(ISettingsStore settings, ISecretStore secrets, IHistoryPolicy policy)
        : this(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "GrpCurlNet.Studio", "history.ndjson"),
            secrets, settings, policy) { }

    internal JsonHistoryStore(string path, ISecretStore secrets, ISettingsStore? settings = null,
        IHistoryPolicy? policy = null, int maxEntries = 1000, long maxBytes = 50L * 1024 * 1024,
        SecureSecretFile? files = null, Action<string, byte[]>? observe = null, TimeSpan? lockTimeout = null)
    {
        _path = path;
        _settings = settings;
        _defaults = new() { MaxEntries = maxEntries, MaxBytes = maxBytes };
        _policy = policy ?? new HistoryPolicy();
        _files = files ?? new();
        _cipher = new(secrets, observe);
        _lockTimeout = lockTimeout ?? TimeSpan.FromSeconds(5);
    }

    private HistorySettings Settings => _settings?.Current.History ?? _defaults;
    public string? Message { get; private set; }

    public Task AppendAsync(HistoryEntry entry, CancellationToken cancellationToken = default)
        => RunAsync(async token =>
        {
            if (!Settings.Enabled || !_policy.AllowHistory) { Message = _policy.Message; return; }
            var safe = await HistorySanitizer.SanitizeAsync(entry, Settings, _policy, token).ConfigureAwait(false);
            var (entries, key) = await ReadAsync(token, needKey: true).ConfigureAwait(false);
            try
            {
                var line = _cipher.Protect(Serialize(safe), key!);
                _files.Append(_path, line + "\n");
                entries = [.. entries, safe];
                var sizes = entries.Select(e => HistoryCipher.EnvelopeLength(Encoding.UTF8.GetByteCount(Serialize(e))) + 1).ToList();
                var bytes = sizes.Sum(size => (long)size);
                var changed = false;
                while ((entries.Count > Math.Max(1, Settings.MaxEntries) || bytes > Math.Max(1, Settings.MaxBytes))
                    && entries.FindIndex(e => !e.Pinned) is var index and >= 0)
                { bytes -= sizes[index]; sizes.RemoveAt(index); entries.RemoveAt(index); changed = true; }
                if (changed) { Rewrite(entries, key!); }
            }
            finally { if (key is not null) { CryptographicOperations.ZeroMemory(key); } InvalidateCache(); }
        }, cancellationToken);

    public async Task<IReadOnlyList<HistoryEntry>> ReadAllAsync(CancellationToken cancellationToken = default)
    {
        IReadOnlyList<HistoryEntry> result = [];
        await RunAsync(async token =>
        {
            var (entries, key) = await ReadAsync(token).ConfigureAwait(false);
            try { result = entries.AsReadOnly(); }
            finally { if (key is not null) { CryptographicOperations.ZeroMemory(key); } }
        }, cancellationToken).ConfigureAwait(false);
        return result;
    }

    public Task SetPinnedAsync(string id, bool pinned, CancellationToken cancellationToken = default)
        => MutateAsync(entries => entries.Select(e => e.Id == id ? e with { Pinned = pinned } : e).ToList(), cancellationToken);
    public Task DeleteAsync(IReadOnlyCollection<string> ids, CancellationToken cancellationToken = default)
        => MutateAsync(entries => entries.Where(e => !ids.Contains(e.Id)).ToList(), cancellationToken);
    public Task ClearAsync(bool keepPinned = false, CancellationToken cancellationToken = default)
        => keepPinned ? MutateAsync(entries => entries.Where(e => e.Pinned).ToList(), cancellationToken)
            : RunAsync(_ => { _files.Delete(_path); InvalidateCache(); return Task.CompletedTask; }, cancellationToken);

    public Task ExportAsync(string path, IReadOnlyList<HistoryEntry> entries, CancellationToken cancellationToken = default)
        => RunAsync(_ =>
        {
            // Revalidate source/key before exporting cached UI rows; never export through a recovery failure.
            return ExportCoreAsync(path, entries, cancellationToken);
        }, cancellationToken);

    private async Task ExportCoreAsync(string path, IReadOnlyList<HistoryEntry> selected, CancellationToken token)
    {
        if (string.Equals(Path.GetDirectoryName(Path.GetFullPath(path)), Path.GetDirectoryName(Path.GetFullPath(_path)), OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
        { throw new HistoryStorageException("Choose an export file outside the Studio storage directory."); }
        var (entries, key) = await ReadAsync(token).ConfigureAwait(false);
        try
        {
            var ids = selected.Select(e => e.Id).ToHashSet(StringComparer.Ordinal);
            _files.Export(path, string.Concat(entries.Where(e => ids.Contains(e.Id)).Select(e => Serialize(e) + "\n")));
        }
        finally { if (key is not null) { CryptographicOperations.ZeroMemory(key); } }
    }

    private Task MutateAsync(Func<List<HistoryEntry>, List<HistoryEntry>> transform, CancellationToken token)
        => RunAsync(async ct =>
        {
            var (entries, key) = await ReadAsync(ct).ConfigureAwait(false);
            try { if (entries.Count > 0) { Rewrite(transform(entries), key!); } }
            finally { if (key is not null) { CryptographicOperations.ZeroMemory(key); } InvalidateCache(); }
        }, token);

    private async Task<(List<HistoryEntry> Entries, byte[]? Key)> ReadAsync(CancellationToken token, bool needKey = false)
    {
        var text = _files.Read(_path);
        if (string.IsNullOrEmpty(text))
        {
            InvalidateCache();
            if (!needKey && Settings.Enabled && _policy.AllowHistory) { await _cipher.CheckAccessAsync(token).ConfigureAwait(false); }
            return ([], needKey ? await _cipher.ResolveKeyAsync(true, token).ConfigureAwait(false) : null);
        }
        var lines = text.Split('\n');
        var tail = lines[^1];
        var complete = lines[..^1].Where(l => !string.IsNullOrWhiteSpace(l)).ToArray();
        // Even an incomplete encrypted tail is evidence of an existing key: never regenerate it.
        var encrypted = lines.Any(l => !string.IsNullOrWhiteSpace(l) && !l.TrimStart().StartsWith("{", StringComparison.Ordinal));
        var key = await _cipher.ResolveKeyAsync(!encrypted, token).ConfigureAwait(false);
        try
        {
            var entries = new List<HistoryEntry>();
            var legacy = false;
            foreach (var line in complete)
            {
                var isEncrypted = line.StartsWith(HistoryCipher.Prefix, StringComparison.Ordinal);
                legacy |= !isEncrypted;
                var json = isEncrypted ? _cipher.Unprotect(line, key) : line;
                entries.Add(ParseEntry(json));
            }
            // A valid final record without a newline is preserved; only syntactically incomplete tails are discarded.
            if (!string.IsNullOrWhiteSpace(tail))
            {
                if (tail.StartsWith(HistoryCipher.Prefix, StringComparison.Ordinal))
                {
                    byte[]? blob = null;
                    try { blob = Convert.FromBase64String(tail[HistoryCipher.Prefix.Length..]); }
                    catch (FormatException)
                    {
                        if (tail[HistoryCipher.Prefix.Length..].Any(c => !char.IsAsciiLetterOrDigit(c) && c is not ('+' or '/' or '=' or '\r')))
                        { throw; }
                    }
                    if (blob is { Length: >= 28 })
                    {
                        entries.Add(ParseEntry(_cipher.Unprotect(tail, key))); // Authentication failure escapes, even at the tail.
                    }
                }
                else if (tail.TrimStart().StartsWith("{", StringComparison.Ordinal))
                {
                    if (IsCompleteJson(tail)) { entries.Add(ParseEntry(tail)); legacy = true; }
                }
                else if (!HistoryCipher.Prefix.StartsWith(tail, StringComparison.Ordinal)) { throw new JsonException(); }
            }
            if (legacy || tail.Length > 0) { Rewrite(entries, key); text = _files.Read(_path) ?? string.Empty; }
            if (_cachedCiphertext == text && _cachedEntries is not null) { return (_cachedEntries, key); }
            if (Volatile.Read(ref _disposed) == 0) { _cachedCiphertext = text; _cachedEntries = entries; }
            return (entries, key);
        }
        catch { CryptographicOperations.ZeroMemory(key); throw; }
    }

    private static HistoryEntry ParseEntry(string json)
    {
        var entry = JsonSerializer.Deserialize(json, HistoryJsonContext.Default.HistoryEntry);
        if (entry is null || entry.V != HistoryEntry.CurrentVersion || entry.Id is null || entry.Method is null
            || entry.Request is null || entry.Request.Body is null || entry.Request.Headers is null
            || entry.Request.Headers.Any(h => h is null || h.Name is null || h.Value is null)
            || entry.Outcome is null || entry.Connection is null || entry.Connection.Name is null)
        { throw new JsonException(); }
        return entry;
    }

    private static bool IsCompleteJson(string text)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        try
        {
            var reader = new Utf8JsonReader(bytes, isFinalBlock: false, state: default);
            var complete = JsonDocument.TryParseValue(ref reader, out var document);
            document?.Dispose();
            return complete; // Invalid syntax throws; only unfinished JSON returns false.
        }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }

    private void Rewrite(List<HistoryEntry> entries, byte[] key)
    {
        if (entries.Count == 0) { _files.Delete(_path); }
        else { _files.Write(_path, string.Concat(entries.Select(e => _cipher.Protect(Serialize(e), key) + "\n"))); }
        InvalidateCache();
    }

    private async Task RunAsync(Func<CancellationToken, Task> operation, CancellationToken token)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        await _gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
            timeout.CancelAfter(_lockTimeout);
            FileStream? handle = null;
            try
            {
                while ((handle = _files.TryOpenExclusive(_path + ".lock")) is null)
                { await Task.Delay(50, timeout.Token).ConfigureAwait(false); }
            }
            catch (OperationCanceledException) when (!token.IsCancellationRequested)
            { throw new HistoryStorageException("History is busy in another Studio process. Retry after that operation finishes."); }
            using (handle)
            {
                token.ThrowIfCancellationRequested();
                Message = _policy.Message;
                await operation(token).ConfigureAwait(false);
            }
        }
        catch (HistoryStorageException ex) { Message = ex.Message; InvalidateCache(); throw; }
        catch (Exception ex) when (ex is SecretBackendUnavailableException or IOException or UnauthorizedAccessException or CryptographicException or FormatException or JsonException)
        {
            InvalidateCache();
            Message = "History could not be protected or read. Restore secret storage access and a consistent history backup, then retry; or explicitly clear all history. Existing history has been retained where the operation had not committed.";
            throw new HistoryStorageException(Message);
        }
        finally { if (Volatile.Read(ref _disposed) != 0) { InvalidateCache(); } _ = _gate.Release(); }
    }

    private static string Serialize(HistoryEntry entry) => JsonSerializer.Serialize(entry, HistoryJsonContext.Default.HistoryEntry);
    private void InvalidateCache() { _cachedEntries = null; _cachedCiphertext = null; }
    public void Dispose() { _ = Interlocked.Exchange(ref _disposed, 1); InvalidateCache(); }
}
