namespace GrpCurl.Net.Studio.ViewModels.Documents;

/// <summary>
///     What <see cref="DocumentsViewModel.DisposeOpenDocumentsAsync" /> actually achieved (PRD-005
///     re-review, finding 1).
/// </summary>
/// <param name="Documents">How many open tabs shutdown released.</param>
/// <param name="Drained">
///     <see langword="true" /> when every cancelled operation unwound before the timeout. When it is
///     <see langword="false" /> the tabs were still disposed, but background work was running when
///     shutdown stopped waiting — a caller that reports "nothing is still running" on the strength of
///     this call is only entitled to say so when this is <see langword="true" />.
/// </param>
public readonly record struct DocumentShutdownResult(int Documents, bool Drained);
