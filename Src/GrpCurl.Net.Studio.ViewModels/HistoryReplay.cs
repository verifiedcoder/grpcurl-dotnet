using System.Globalization;
using GrpCurl.Net.Studio.ViewModels.Models.Connections;
using GrpCurl.Net.Studio.ViewModels.Models.History;
using GrpCurl.Net.Studio.ViewModels.Models.Invocation;

namespace GrpCurl.Net.Studio.ViewModels;

/// <summary>
///     Shared replay mapping for history entries (FR-123), used by the History tab and the command palette:
///     resolves the entry's connection against the current workspace, and rebuilds a
///     <see cref="RequestPrefill" /> from the (redacted) request — a redacted secret comes back by name only,
///     flagged "value required"; <c>${VAR}</c> headers restore verbatim and re-resolve at send time.
/// </summary>
internal static class HistoryReplay
{
    public static SavedConnection? ResolveConnection(WorkspaceModel workspace, HistoryEntry entry)
        => workspace.Connections.FirstOrDefault(c => c.Name == entry.Connection.Name);

    public static RequestPrefill ToPrefill(HistoryRequest request)
    {
        var headers = request.Headers
            .Select(h => h.Value == HistoryEntry.RedactedMarker
                ? new PrefillHeader(h.Name, string.Empty, IsBin(h.Name), RequiresValue: true)
                : new PrefillHeader(h.Name, h.Value, IsBin(h.Name)))
            .ToList();

        return new RequestPrefill(
            request.Body,
            request.BodyFormat == "text" ? RequestBodyFormat.Text : RequestBodyFormat.Json,
            headers,
            request.Deadline,
            request.EmitDefaults,
            request.AllowUnknownFields,
            (request.MaxReceiveBytes ?? request.MaxSendBytes)?.ToString(CultureInfo.InvariantCulture));
    }

    private static bool IsBin(string name) => name.EndsWith("-bin", StringComparison.OrdinalIgnoreCase);
}
