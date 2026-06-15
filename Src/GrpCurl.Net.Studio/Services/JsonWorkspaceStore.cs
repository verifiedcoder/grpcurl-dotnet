using System.Text.Json;
using GrpCurl.Net.Studio.ViewModels.Models.Connections;
using GrpCurl.Net.Studio.ViewModels.Services;

namespace GrpCurl.Net.Studio.Services;

/// <summary>
///     Persists the workspace to a single default <c>workspace.json</c> in the per-OS config
///     directory (SPEC-040 §1, <c>GrpCurlNet.Studio</c>). Phase 1's single-workspace model;
///     E3.1 generalizes to user-chosen paths with open/save/recent. Atomic temp-file write;
///     a corrupt file is set aside and an empty workspace is used so the app always starts.
/// </summary>
internal sealed class JsonWorkspaceStore : IWorkspaceStore
{
    private const string AppFolderName = "GrpCurlNet.Studio";
    private const string FileName = "workspace.json";

    private readonly string _path;

    public JsonWorkspaceStore()
        : this(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            AppFolderName,
            FileName))
    {
    }

    // Test seam: point the store at a temp file instead of the real config dir.
    internal JsonWorkspaceStore(string path) => _path = path;

    public WorkspaceModel Current { get; private set; } = WorkspaceModel.Empty();

    public async Task<WorkspaceModel> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_path))
        {
            return Current = WorkspaceModel.Empty();
        }

        try
        {
            await using var stream = File.OpenRead(_path);

            Current = await JsonSerializer.DeserializeAsync(
                          stream,
                          WorkspaceJsonContext.Default.WorkspaceModel,
                          cancellationToken).ConfigureAwait(false)
                      ?? WorkspaceModel.Empty();
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            TryQuarantine();
            Current = WorkspaceModel.Empty();
        }

        return Current;
    }

    public async Task SaveAsync(WorkspaceModel workspace, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);

        var tempPath = _path + ".tmp";

        await using (var stream = File.Create(tempPath))
        {
            await JsonSerializer.SerializeAsync(
                stream,
                workspace,
                WorkspaceJsonContext.Default.WorkspaceModel,
                cancellationToken).ConfigureAwait(false);
        }

        File.Move(tempPath, _path, overwrite: true);
        Current = workspace;
    }

    private void TryQuarantine()
    {
        try
        {
            File.Move(_path, _path + ".corrupt", overwrite: true);
        }
        catch (IOException)
        {
            // Best effort.
        }
    }
}
