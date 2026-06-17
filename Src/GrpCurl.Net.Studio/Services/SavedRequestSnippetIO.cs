using System.Text.Json;
using System.Text.Json.Serialization;
using GrpCurl.Net.Studio.ViewModels.Models.Connections;
using GrpCurl.Net.Studio.ViewModels.Services;

namespace GrpCurl.Net.Studio.Services;

/// <summary>
///     Default <see cref="ISavedRequestSnippetIO" /> (FR-166): a single saved request wrapped in a small
///     <c>{kind, v, request}</c> envelope, written camelCase with LF endings like the workspace file. The
///     snippet is secret-free (FR-141) — only literals and <c>${VAR}</c> placeholders.
/// </summary>
internal sealed class SavedRequestSnippetIO : ISavedRequestSnippetIO
{
    private const string KindMarker = "grpcn.savedRequest";
    private const int CurrentVersion = 1;

    public async Task ExportAsync(SavedRequest request, string path, CancellationToken cancellationToken = default)
    {
        var snippet = new SavedRequestSnippet(KindMarker, CurrentVersion, request);
        var json = JsonSerializer.Serialize(snippet, SavedRequestSnippetJsonContext.Default.SavedRequestSnippet)
            .ReplaceLineEndings("\n");

        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await File.WriteAllTextAsync(path, json, cancellationToken).ConfigureAwait(false);
    }

    public async Task<SavedRequest> ImportAsync(string path, CancellationToken cancellationToken = default)
    {
        string json;

        try
        {
            json = await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
        }
        catch (IOException ex)
        {
            throw new SavedRequestSnippetException($"Could not read '{Path.GetFileName(path)}': {ex.Message}");
        }

        SavedRequestSnippet? snippet;

        try
        {
            snippet = JsonSerializer.Deserialize(json, SavedRequestSnippetJsonContext.Default.SavedRequestSnippet);
        }
        catch (JsonException ex)
        {
            throw new SavedRequestSnippetException($"'{Path.GetFileName(path)}' is not a valid request snippet: {ex.Message}");
        }

        if (snippet is null || snippet.Kind != KindMarker || snippet.Request is null)
        {
            throw new SavedRequestSnippetException($"'{Path.GetFileName(path)}' is not a saved-request snippet.");
        }

        if (snippet.V > CurrentVersion)
        {
            throw new SavedRequestSnippetException($"'{Path.GetFileName(path)}' was created by a newer version of Studio.");
        }

        return snippet.Request;
    }
}

/// <summary>The on-disk snippet envelope (FR-166).</summary>
internal sealed record SavedRequestSnippet(string Kind, int V, SavedRequest Request);

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    WriteIndented = true,
    UseStringEnumConverter = true)]
[JsonSerializable(typeof(SavedRequestSnippet))]
internal sealed partial class SavedRequestSnippetJsonContext : JsonSerializerContext;
