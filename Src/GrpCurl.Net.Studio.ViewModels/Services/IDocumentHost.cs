using GrpCurl.Net.Studio.ViewModels.Models.Connections;

namespace GrpCurl.Net.Studio.ViewModels.Services;

/// <summary>
///     Opens centre-zone document tabs. E1.3 exposes describe; E1.4 adds invocation. The explorer
///     and describe documents call this to open/navigate tabs without referencing the document
///     collection directly.
/// </summary>
public interface IDocumentHost
{
    /// <summary>
    ///     Opens a describe tab for <paramref name="symbol" /> on <paramref name="connection" />.
    ///     When <paramref name="newTab" /> is <see langword="false" /> an existing tab already showing
    ///     that symbol is selected instead of opening a duplicate (FR-051 Ctrl+click → new tab).
    /// </summary>
    void OpenDescribe(SavedConnection connection, string symbol, bool newTab = false);

    /// <summary>
    ///     Opens a new invocation tab bound to <paramref name="methodSymbol" /> on
    ///     <paramref name="connection" /> (FR-053 / FR-024 New request). When
    ///     <paramref name="initialRequestJson" /> is <see langword="null" /> the tab generates the
    ///     request template itself. Invocation tabs are editable drafts, so each call opens a new one.
    /// </summary>
    void OpenInvocation(SavedConnection connection, string methodSymbol, string? initialRequestJson = null);
}
