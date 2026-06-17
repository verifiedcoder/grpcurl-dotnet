using GrpCurl.Net.Studio.ViewModels.Models.Connections;
using GrpCurl.Net.Studio.ViewModels.Services;

namespace GrpCurl.Net.Studio.TestSupport;

/// <summary>In-memory <see cref="ISavedRequestSnippetIO" />: records exports and returns a scripted import.</summary>
public sealed class FakeSavedRequestSnippetIO : ISavedRequestSnippetIO
{
    public (SavedRequest Request, string Path)? LastExport { get; private set; }

    /// <summary>The request <see cref="ImportAsync" /> returns; set <see cref="ImportError" /> to throw instead.</summary>
    public SavedRequest? ImportResult { get; set; }

    public Exception? ImportError { get; set; }

    public Task ExportAsync(SavedRequest request, string path, CancellationToken cancellationToken = default)
    {
        LastExport = (request, path);
        return Task.CompletedTask;
    }

    public Task<SavedRequest> ImportAsync(string path, CancellationToken cancellationToken = default)
    {
        if (ImportError is not null)
        {
            throw ImportError;
        }

        return Task.FromResult(ImportResult ?? throw new SavedRequestSnippetException("no scripted import result"));
    }
}
