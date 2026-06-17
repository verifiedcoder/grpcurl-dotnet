using GrpCurl.Net.Studio.ViewModels.Models.Connections;

namespace GrpCurl.Net.Studio.ViewModels.Services;

/// <summary>
///     Exports/imports a single <see cref="SavedRequest" /> as a standalone JSON snippet for ad-hoc sharing
///     (FR-166). The snippet is secret-free like the workspace (FR-141): header values are literals or
///     <c>${VAR}</c> placeholders, never secret values.
/// </summary>
public interface ISavedRequestSnippetIO
{
    /// <summary>Writes <paramref name="request" /> to <paramref name="path" /> as a snippet file.</summary>
    Task ExportAsync(SavedRequest request, string path, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Reads a snippet file. Throws <see cref="SavedRequestSnippetException" /> when the file is not a
    ///     valid request snippet (wrong kind, newer version, or corrupt).
    /// </summary>
    Task<SavedRequest> ImportAsync(string path, CancellationToken cancellationToken = default);
}

/// <summary>Thrown when a file is not a readable saved-request snippet (FR-166).</summary>
public sealed class SavedRequestSnippetException(string message) : Exception(message);
