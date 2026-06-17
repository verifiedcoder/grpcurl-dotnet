using System.Text.Json;
using GrpCurl.Net.Studio.ViewModels.Models.Connections;
using GrpCurl.Net.Studio.ViewModels.Services;

namespace GrpCurl.Net.Studio.Services;

/// <summary>
///     Persists workspaces as <c>.gcnws.json</c> documents (SPEC-040 §3) and tracks the recently used
///     paths in <c>recent-workspaces.json</c> (§1). The default startup workspace lives in the per-OS
///     config directory and loads resiliently; <see cref="OpenAsync" /> reads documents from anywhere
///     strictly (surfacing <see cref="WorkspaceSchemaException" />). All writes are atomic temp-file moves
///     through <see cref="WorkspaceSerializer" /> (canonical LF / no-BOM format).
/// </summary>
internal sealed class JsonWorkspaceStore : IWorkspaceStore
{
    private const string AppFolderName = "GrpCurlNet.Studio";
    private const string DefaultFileName = "workspace.json";
    private const string RecentFileName = "recent-workspaces.json";
    private const int MaxRecent = 10;

    private readonly string _defaultPath;
    private readonly string _recentPath;
    private readonly List<string> _recent = [];

    public JsonWorkspaceStore()
        : this(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            AppFolderName,
            DefaultFileName))
    {
    }

    // Test seam: point the store at a temp file; recents live beside it.
    internal JsonWorkspaceStore(string path)
    {
        _defaultPath = path;
        CurrentPath = path;
        _recentPath = Path.Combine(Path.GetDirectoryName(path) ?? ".", RecentFileName);
        LoadRecent();
    }

    public WorkspaceModel Current { get; private set; } = WorkspaceModel.Empty();

    public string? CurrentPath { get; private set; }

    public IReadOnlyList<RecentWorkspace> RecentWorkspaces
        => _recent.Select(p => new RecentWorkspace(p, File.Exists(p))).ToList();

    public async Task<WorkspaceModel> LoadAsync(CancellationToken cancellationToken = default)
    {
        CurrentPath = _defaultPath;

        if (!File.Exists(_defaultPath))
        {
            return Current = WorkspaceModel.Empty();
        }

        try
        {
            var json = await File.ReadAllTextAsync(_defaultPath, cancellationToken).ConfigureAwait(false);
            Current = WorkspaceSerializer.Deserialize(json);
        }
        catch (Exception ex) when (ex is WorkspaceSchemaException or IOException)
        {
            // The default startup workspace stays resilient: a corrupt/newer file is set aside so the app
            // always starts. OpenAsync is the strict, user-facing path that surfaces the error instead.
            TryQuarantine(_defaultPath);
            Current = WorkspaceModel.Empty();
        }

        return Current;
    }

    public async Task<WorkspaceModel> OpenAsync(string path, CancellationToken cancellationToken = default)
    {
        var json = await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);

        // Strict: WorkspaceSerializer throws WorkspaceSchemaException for a corrupt/newer file. We let it
        // propagate so the open flow can show the message, leaving Current/CurrentPath untouched.
        var workspace = WorkspaceSerializer.Deserialize(json);

        Current = workspace;
        CurrentPath = path;
        await PromoteRecentAsync(path, cancellationToken).ConfigureAwait(false);
        return workspace;
    }

    public async Task SaveAsync(WorkspaceModel workspace, CancellationToken cancellationToken = default)
    {
        await WriteAtomicAsync(CurrentPath ?? _defaultPath, workspace, cancellationToken).ConfigureAwait(false);
        Current = workspace;
        CurrentPath ??= _defaultPath;
    }

    public async Task SaveAsAsync(WorkspaceModel workspace, string path, CancellationToken cancellationToken = default)
    {
        await WriteAtomicAsync(path, workspace, cancellationToken).ConfigureAwait(false);
        Current = workspace;
        CurrentPath = path;
        await PromoteRecentAsync(path, cancellationToken).ConfigureAwait(false);
    }

    public WorkspaceModel NewWorkspace()
    {
        Current = WorkspaceModel.Empty();
        CurrentPath = null; // untitled until the first Save As
        return Current;
    }

    public async Task RemoveRecentAsync(string path, CancellationToken cancellationToken = default)
    {
        if (_recent.RemoveAll(p => PathsEqual(p, path)) > 0)
        {
            await PersistRecentAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task WriteAtomicAsync(string path, WorkspaceModel workspace, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        var tempPath = path + ".tmp";
        await File.WriteAllBytesAsync(tempPath, WorkspaceSerializer.SerializeToUtf8(workspace), cancellationToken).ConfigureAwait(false);

        File.Move(tempPath, path, overwrite: true);
    }

    private async Task PromoteRecentAsync(string path, CancellationToken cancellationToken)
    {
        var full = Path.GetFullPath(path);
        _recent.RemoveAll(p => PathsEqual(p, full));
        _recent.Insert(0, full);

        if (_recent.Count > MaxRecent)
        {
            _recent.RemoveRange(MaxRecent, _recent.Count - MaxRecent);
        }

        await PersistRecentAsync(cancellationToken).ConfigureAwait(false);
    }

    private void LoadRecent()
    {
        if (!File.Exists(_recentPath))
        {
            return;
        }

        try
        {
            var paths = JsonSerializer.Deserialize(File.ReadAllText(_recentPath), RecentWorkspacesJsonContext.Default.ListString);

            if (paths is not null)
            {
                _recent.AddRange(paths.Where(p => !string.IsNullOrWhiteSpace(p)).Take(MaxRecent));
            }
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            // Cosmetic data: a corrupt recents file is simply ignored (regenerated empty on next write).
        }
    }

    private async Task PersistRecentAsync(CancellationToken cancellationToken)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_recentPath)!);
            var json = JsonSerializer.Serialize(_recent, RecentWorkspacesJsonContext.Default.ListString);
            await File.WriteAllTextAsync(_recentPath, json, cancellationToken).ConfigureAwait(false);
        }
        catch (IOException)
        {
            // Best effort — the recents list is a convenience, never load-bearing.
        }
    }

    private static bool PathsEqual(string a, string b)
        => string.Equals(Path.GetFullPath(a), Path.GetFullPath(b),
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    private static void TryQuarantine(string path)
    {
        try
        {
            File.Move(path, path + ".corrupt", overwrite: true);
        }
        catch (IOException)
        {
            // Best effort.
        }
    }
}
