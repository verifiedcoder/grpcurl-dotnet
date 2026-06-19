using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using GrpCurl.Net.Studio.ViewModels.Models.Diagnostics;
using GrpCurl.Net.Studio.ViewModels.Services;

namespace GrpCurl.Net.Studio.Services;

/// <summary>
///     Default <see cref="IDiagnosticsLog" /> (SPEC-030 §9, FR-155): an append-only NDJSON file under the
///     app-data directory with 7-day / 10 MB retention (oldest evicted first). Writes are best-effort and
///     never throw into the caller, so logging can't break the app. Header values are never written — call
///     sites pass names only (SEC-031).
/// </summary>
internal sealed class FileDiagnosticsLog : IDiagnosticsLog
{
    private const string AppFolderName = "GrpCurlNet.Studio";
    private const string FileName = "diagnostics.ndjson";

    private static readonly Encoding Utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
    private static readonly TimeSpan MaxAge = TimeSpan.FromDays(7);

    private readonly string _path;
    private readonly long _maxBytes;
    private readonly TimeSpan _maxAge;
    private readonly Func<DateTimeOffset> _now;
    private readonly System.Threading.Lock _gate = new();

    private int _sinceCheck;

    public FileDiagnosticsLog()
        : this(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), AppFolderName, FileName))
    {
    }

    // Test seam: a temp file with (optionally) tighter caps + a fake clock to exercise retention.
    internal FileDiagnosticsLog(string path, long maxBytes = 10L * 1024 * 1024, TimeSpan? maxAge = null, Func<DateTimeOffset>? now = null)
    {
        _path = path;
        _maxBytes = Math.Max(1, maxBytes);
        _maxAge = maxAge ?? MaxAge;
        _now = now ?? (() => DateTimeOffset.UtcNow);
    }

    public string LogFolderPath => Path.GetDirectoryName(_path) ?? ".";

    public string LogFilePath => _path;

    public void Log(DiagnosticsLevel level, string category, string message)
    {
        var entry = new DiagnosticsLogEntry(_now(), level, category, message);

        lock (_gate)
        {
            try
            {
                Directory.CreateDirectory(LogFolderPath);
                File.AppendAllText(_path, Serialize(entry) + "\n", Utf8NoBom);

                // Enforce periodically rather than on every line (logging is the hot path).
                if (++_sinceCheck >= 64)
                {
                    _sinceCheck = 0;
                    EnforceRetentionLocked();
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Best-effort: logging must never break the app.
            }
        }
    }

    public Task<IReadOnlyList<DiagnosticsLogEntry>> ReadRecentAsync(CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            EnforceRetentionLocked();
            return Task.FromResult<IReadOnlyList<DiagnosticsLogEntry>>(ReadAllLocked());
        }
    }

    private List<DiagnosticsLogEntry> ReadAllLocked()
    {
        if (!File.Exists(_path))
        {
            return [];
        }

        var entries = new List<DiagnosticsLogEntry>();

        foreach (var line in File.ReadLines(_path, Utf8NoBom))
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            try
            {
                if (JsonSerializer.Deserialize(line, DiagnosticsJsonContext.Default.DiagnosticsLogEntry) is { } entry)
                {
                    entries.Add(entry);
                }
            }
            catch (JsonException)
            {
                // Skip a torn/partial line rather than failing the whole read.
            }
        }

        return entries;
    }

    private void EnforceRetentionLocked()
    {
        if (!File.Exists(_path))
        {
            return;
        }

        var entries = ReadAllLocked();
        var cutoff = _now() - _maxAge;
        var kept = entries.Where(e => e.At >= cutoff).ToList();

        // Then trim oldest-first until within the byte cap.
        var lines = kept.Select(Serialize).ToList();
        var bytes = lines.Sum(l => (long)Utf8NoBom.GetByteCount(l) + 1);

        var start = 0;
        while (start < lines.Count && bytes > _maxBytes)
        {
            bytes -= Utf8NoBom.GetByteCount(lines[start]) + 1;
            start++;
        }

        if (start == 0 && kept.Count == entries.Count)
        {
            return; // nothing evicted
        }

        var survivors = lines.Skip(start);
        File.WriteAllText(_path, survivors.Any() ? string.Join('\n', survivors) + "\n" : string.Empty, Utf8NoBom);
    }

    private static string Serialize(DiagnosticsLogEntry entry)
        => JsonSerializer.Serialize(entry, DiagnosticsJsonContext.Default.DiagnosticsLogEntry);
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, UseStringEnumConverter = true)]
[JsonSerializable(typeof(DiagnosticsLogEntry))]
internal sealed partial class DiagnosticsJsonContext : JsonSerializerContext;
