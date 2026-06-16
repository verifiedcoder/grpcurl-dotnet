namespace GrpCurl.Net.Studio.ViewModels.Models.Descriptors;

/// <summary>How a schema export concluded (FR-100..104).</summary>
public enum SchemaExportOutcome
{
    /// <summary>Files were written.</summary>
    Success,

    /// <summary>Target file(s) already exist and overwrite wasn't confirmed — the caller must confirm and retry.</summary>
    Conflict,

    /// <summary>The export failed (schema/IO error).</summary>
    Failure
}

/// <summary>A file written by an export (FR-103 result summary).</summary>
public sealed record ExportedFile(string Path, long SizeBytes);

/// <summary>An existing file that an export would overwrite (FR-101/102 overwrite dialog).</summary>
public sealed record FileConflict(string Path, long SizeBytes, DateTime ModifiedUtc);

/// <summary>
///     Outcome of exporting a connection's active descriptor set to a protoset file or reconstructed
///     <c>.proto</c> directory (FR-100..104). On <see cref="SchemaExportOutcome.Conflict" /> nothing was
///     written; the caller confirms the overwrite and re-runs with overwrite enabled.
/// </summary>
public sealed record SchemaExportResult(
    SchemaExportOutcome Outcome,
    IReadOnlyList<ExportedFile> Written,
    IReadOnlyList<FileConflict> Conflicts,
    TimeSpan Duration,
    string? ErrorMessage)
{
    public bool Ok => Outcome == SchemaExportOutcome.Success;

    public static SchemaExportResult Success(IReadOnlyList<ExportedFile> written, TimeSpan duration)
        => new(SchemaExportOutcome.Success, written, [], duration, null);

    public static SchemaExportResult Conflict(IReadOnlyList<FileConflict> conflicts)
        => new(SchemaExportOutcome.Conflict, [], conflicts, TimeSpan.Zero, null);

    public static SchemaExportResult Failure(string message)
        => new(SchemaExportOutcome.Failure, [], [], TimeSpan.Zero, message);
}
