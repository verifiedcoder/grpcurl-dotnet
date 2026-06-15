using GrpCurl.Net.Studio.ViewModels.Models;
using GrpCurl.Net.Studio.ViewModels.Services;

namespace GrpCurl.Net.Studio.Services;

// Minimal service implementations for the skeleton. Real implementations (native dialogs,
// file pickers, clipboard, JSON-backed settings) arrive with the features that need them;
// for now they keep the DI graph complete so the shell composes and runs.

internal sealed class NoopDialogService : IDialogService
{
    public Task ShowMessageAsync(string title, string message, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public Task<bool> ConfirmAsync(string title, string message, CancellationToken cancellationToken = default)
        => Task.FromResult(false);
}

internal sealed class NoopFilePickerService : IFilePickerService
{
    public Task<string?> OpenFileAsync(string title, IReadOnlyList<string>? extensions = null, CancellationToken cancellationToken = default)
        => Task.FromResult<string?>(null);

    public Task<string?> SaveFileAsync(string title, string? suggestedName = null, IReadOnlyList<string>? extensions = null, CancellationToken cancellationToken = default)
        => Task.FromResult<string?>(null);
}

internal sealed class NoopClipboardService : IClipboardService
{
    public Task SetTextAsync(string text, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public Task<string?> GetTextAsync(CancellationToken cancellationToken = default)
        => Task.FromResult<string?>(null);
}

/// <summary>
///     In-memory settings store for the skeleton: serves defaults and ignores saves. Replaced
///     by the JSON-backed store when theme persistence lands (E0.2 PR-B).
/// </summary>
internal sealed class InMemorySettingsStore : ISettingsStore
{
    public StudioSettings Current { get; private set; } = StudioSettings.Defaults();

    public Task<StudioSettings> LoadAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(Current);

    public Task SaveAsync(StudioSettings settings, CancellationToken cancellationToken = default)
    {
        Current = settings;
        return Task.CompletedTask;
    }
}
