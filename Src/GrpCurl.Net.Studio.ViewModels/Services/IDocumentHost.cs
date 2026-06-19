using GrpCurl.Net.Studio.ViewModels.Models.Connections;
using GrpCurl.Net.Studio.ViewModels.Models.Invocation;

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

    /// <summary>
    ///     Opens a new invocation tab pre-filled from <paramref name="prefill" /> (FR-123 replay): body,
    ///     format, headers, and options, without binding the tab to a saved request (it is a fresh draft).
    /// </summary>
    void OpenInvocation(SavedConnection connection, string methodSymbol, RequestPrefill prefill);

    /// <summary>
    ///     Opens a saved request (FR-145) into an invocation tab pre-filled with its body, format, headers,
    ///     and options, titled with the request name.
    /// </summary>
    void OpenSavedRequest(SavedConnection connection, SavedRequest request);

    /// <summary>
    ///     Opens a new GraphQL operation tab bound to <paramref name="connection" /> (SPEC-015 E4.1,
    ///     "New GraphQL Operation"). GraphQL tabs are editable drafts, so each call opens a new one.
    /// </summary>
    void OpenGraphQl(SavedConnection connection);

    /// <summary>Opens the Settings tab (FR-150), or focuses it if already open (single instance).</summary>
    void OpenSettings();

    /// <summary>Opens the History tab (FR-120..129), or focuses it if already open (single instance).</summary>
    void OpenHistory();
}
