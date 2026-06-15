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
}
