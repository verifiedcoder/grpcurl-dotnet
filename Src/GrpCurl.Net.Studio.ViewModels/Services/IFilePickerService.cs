namespace GrpCurl.Net.Studio.ViewModels.Services;

/// <summary>
///     Surfaces native open/save file dialogs as an abstraction so view models stay
///     UI-thread-free and testable (SPEC-030 §4). A returned <see langword="null" /> means
///     the user cancelled.
/// </summary>
public interface IFilePickerService
{
    Task<string?> OpenFileAsync(string title, IReadOnlyList<string>? extensions = null, CancellationToken cancellationToken = default);

    /// <summary>Picks one or more files (e.g. multiple protosets or .proto files); empty list on cancel.</summary>
    Task<IReadOnlyList<string>> OpenFilesAsync(string title, IReadOnlyList<string>? extensions = null, CancellationToken cancellationToken = default);

    /// <summary>Picks a single directory (e.g. a protoc import path); null on cancel.</summary>
    Task<string?> OpenFolderAsync(string title, CancellationToken cancellationToken = default);

    Task<string?> SaveFileAsync(string title, string? suggestedName = null, IReadOnlyList<string>? extensions = null, CancellationToken cancellationToken = default);
}
