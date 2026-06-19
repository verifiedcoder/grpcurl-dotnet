using GrpCurl.Net.Studio.ViewModels.Models;
using GrpCurl.Net.Studio.ViewModels.Services;
using System.Text.Json;

namespace GrpCurl.Net.Studio.Services;

/// <summary>
///     Persists <see cref="StudioSettings" /> to <c>settings.json</c> in the per-OS application
///     config directory (SPEC-040 §1: <c>GrpCurlNet.Studio</c> under
///     <see cref="Environment.SpecialFolder.ApplicationData" />, which honours
///     <c>XDG_CONFIG_HOME</c> on Linux). Writes are atomic (temp file + move). A corrupt file
///     is set aside and defaults are used, so the app always starts.
/// </summary>
internal sealed class JsonSettingsStore : ISettingsStore
{
    private const string AppFolderName = "GrpCurlNet.Studio";
    private const string FileName = "settings.json";

    private readonly string _path;

    public JsonSettingsStore()
        : this(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            AppFolderName,
            FileName))
    {
    }

    // Test seam: lets a test point the store at a temp file instead of the real config dir.
    internal JsonSettingsStore(string path) => _path = path;

    public event EventHandler? Changed;

    public StudioSettings Current { get; private set; } = StudioSettings.Defaults();

    public async Task<StudioSettings> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_path))
        {
            return Current = StudioSettings.Defaults();
        }

        try
        {
            await using var stream = File.OpenRead(_path);

            Current = await JsonSerializer.DeserializeAsync(
                          stream,
                          StudioSettingsJsonContext.Default.StudioSettings,
                          cancellationToken).ConfigureAwait(false)
                      ?? StudioSettings.Defaults();
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            // Corrupt/locked file: set it aside (best-effort) and fall back to defaults.
            TryQuarantine();
            Current = StudioSettings.Defaults();
        }

        return Current;
    }

    public async Task SaveAsync(StudioSettings settings, CancellationToken cancellationToken = default)
    {
        _ = Directory.CreateDirectory(Path.GetDirectoryName(_path)!);

        var tempPath = _path + ".tmp";

        await using (var stream = File.Create(tempPath))
        {
            await JsonSerializer.SerializeAsync(
                stream,
                settings,
                StudioSettingsJsonContext.Default.StudioSettings,
                cancellationToken).ConfigureAwait(false);
        }

        File.Move(tempPath, _path, overwrite: true);
        Current = settings;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void TryQuarantine()
    {
        try
        {
            File.Move(_path, _path + ".corrupt", overwrite: true);
        }
        catch (IOException)
        {
            // Best effort only.
        }
    }
}
