using GrpCurl.Net.Studio.ViewModels.Services;

namespace GrpCurl.Net.Studio.TestSupport;

public sealed class FakeFilePickerService : IFilePickerService
{
    public string? OpenResult { get; set; }

    public string? SaveResult { get; set; }

    public string? LastSaveSuggestedName { get; private set; }

    public Task<string?> OpenFileAsync(string title, IReadOnlyList<string>? extensions = null, CancellationToken cancellationToken = default)
        => Task.FromResult(OpenResult);

    public Task<string?> SaveFileAsync(string title, string? suggestedName = null, IReadOnlyList<string>? extensions = null, CancellationToken cancellationToken = default)
    {
        LastSaveSuggestedName = suggestedName;
        return Task.FromResult(SaveResult);
    }
}
