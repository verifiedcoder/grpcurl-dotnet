using GrpCurl.Net.Studio.ViewModels.Models.Diagnostics;

namespace GrpCurl.Net.Studio.ViewModels.Services;

/// <summary>
///     The diagnostics log sink (SPEC-030 §9, FR-155): a rolling file under app-data with 7-day / 10 MB
///     retention. Header values are never written here (SEC-031); call sites pass names only. The in-app
///     viewer (Settings → Diagnostics) reads it back through <see cref="ReadRecentAsync" />.
/// </summary>
public interface IDiagnosticsLog
{
    /// <summary>Appends an entry (best-effort; a write failure is swallowed so logging never breaks the app).</summary>
    void Log(DiagnosticsLevel level, string category, string message);

    /// <summary>Reads the retained entries, oldest first.</summary>
    Task<IReadOnlyList<DiagnosticsLogEntry>> ReadRecentAsync(CancellationToken cancellationToken = default);

    /// <summary>The directory holding the log file (for "Open log folder", FR-155).</summary>
    string LogFolderPath { get; }

    /// <summary>The log file's full path.</summary>
    string LogFilePath { get; }
}
