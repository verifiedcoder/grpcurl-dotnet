using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using GrpCurl.Net.Studio.ViewModels.Services;

namespace GrpCurl.Net.Studio.Services;

/// <summary>
///     Native open/save dialogs via the active window's <see cref="IStorageProvider" />. Returns the
///     local filesystem path (or <see langword="null" /> on cancel / a non-local pick), keeping view
///     models off the UI thread and free of Avalonia storage types (SPEC-030 §4).
/// </summary>
internal sealed class FilePickerService : IFilePickerService
{
    public async Task<string?> OpenFileAsync(string title, IReadOnlyList<string>? extensions = null, CancellationToken cancellationToken = default)
    {
        if (TopLevel() is not { StorageProvider: { } storage })
        {
            return null;
        }

        var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
            FileTypeFilter = FileTypes(extensions)
        });

        return files.Count > 0 ? files[0].TryGetLocalPath() : null;
    }

    public async Task<IReadOnlyList<string>> OpenFilesAsync(string title, IReadOnlyList<string>? extensions = null, CancellationToken cancellationToken = default)
    {
        if (TopLevel() is not { StorageProvider: { } storage })
        {
            return [];
        }

        var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = true,
            FileTypeFilter = FileTypes(extensions)
        });

        return files.Select(f => f.TryGetLocalPath()).Where(p => p is not null).Select(p => p!).ToList();
    }

    public async Task<string?> OpenFolderAsync(string title, CancellationToken cancellationToken = default)
    {
        if (TopLevel() is not { StorageProvider: { } storage })
        {
            return null;
        }

        var folders = await storage.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = title,
            AllowMultiple = false
        });

        return folders.Count > 0 ? folders[0].TryGetLocalPath() : null;
    }

    public async Task<string?> SaveFileAsync(string title, string? suggestedName = null, IReadOnlyList<string>? extensions = null, CancellationToken cancellationToken = default)
    {
        if (TopLevel() is not { StorageProvider: { } storage })
        {
            return null;
        }

        var file = await storage.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = title,
            SuggestedFileName = suggestedName,
            FileTypeChoices = FileTypes(extensions)
        });

        return file?.TryGetLocalPath();
    }

    private static IReadOnlyList<FilePickerFileType>? FileTypes(IReadOnlyList<string>? extensions)
    {
        if (extensions is null || extensions.Count == 0)
        {
            return null;
        }

        var patterns = extensions.Select(e => "*" + (e.StartsWith('.') ? e : "." + e)).ToList();
        return [new FilePickerFileType("Supported files") { Patterns = patterns }];
    }

    private static TopLevel? TopLevel()
        => (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
}
