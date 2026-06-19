using GrpCurl.Net.Studio.ViewModels.Models.Session;
using GrpCurl.Net.Studio.ViewModels.Services;
using System.Text.Json;

namespace GrpCurl.Net.Studio.Services;

/// <summary>
///     Persists the UI session (FR-146) to <c>ui-state.json</c> in the per-OS config directory — machine-local,
///     deliberately outside the workspace file (FR-141). Mirrors <see cref="JsonSettingsStore" />: atomic
///     temp-file writes and a tolerant read where a missing/corrupt file yields an empty session.
/// </summary>
internal sealed class JsonSessionStore : ISessionStore
{
    private const string AppFolderName = "GrpCurlNet.Studio";
    private const string FileName = "ui-state.json";

    private readonly string _path;

    public JsonSessionStore()
        : this(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            AppFolderName,
            FileName))
    {
    }

    internal JsonSessionStore(string path) => _path = path;

    public async Task<SessionState> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_path))
        {
            return new SessionState();
        }

        try
        {
            await using var stream = File.OpenRead(_path);

            return await JsonSerializer.DeserializeAsync(
                       stream,
                       SessionStateJsonContext.Default.SessionState,
                       cancellationToken).ConfigureAwait(false)
                   ?? new SessionState();
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            return new SessionState();
        }
    }

    public async Task SaveAsync(SessionState state, CancellationToken cancellationToken = default)
    {
        _ = Directory.CreateDirectory(Path.GetDirectoryName(_path)!);

        var tempPath = _path + ".tmp";

        await using (var stream = File.Create(tempPath))
        {
            await JsonSerializer.SerializeAsync(
                stream,
                state,
                SessionStateJsonContext.Default.SessionState,
                cancellationToken).ConfigureAwait(false);
        }

        File.Move(tempPath, _path, overwrite: true);
    }
}
