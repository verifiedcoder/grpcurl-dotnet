using GrpCurl.Net.Studio.ViewModels.Models.Diagnostics;
using GrpCurl.Net.Studio.ViewModels.Services;

namespace GrpCurl.Net.Studio.TestSupport;

/// <summary>In-memory <see cref="IDiagnosticsLog" /> for the Settings → Diagnostics viewer tests (FR-155).</summary>
public sealed class FakeDiagnosticsLog : IDiagnosticsLog
{
    public List<DiagnosticsLogEntry> Entries { get; } = [];

    public string LogFolderPath { get; set; } = OperatingSystem.IsWindows() ? @"C:\logs" : "/tmp/logs";

    public string LogFilePath => System.IO.Path.Combine(LogFolderPath, "diagnostics.ndjson");

    public void Log(DiagnosticsLevel level, string category, string message)
        => Entries.Add(new DiagnosticsLogEntry(DateTimeOffset.UtcNow, level, category, message));

    public Task<IReadOnlyList<DiagnosticsLogEntry>> ReadRecentAsync(CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<DiagnosticsLogEntry>>(Entries.ToList());
}
