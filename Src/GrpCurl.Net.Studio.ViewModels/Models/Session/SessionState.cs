using GrpCurl.Net.Studio.ViewModels.Models.Invocation;

namespace GrpCurl.Net.Studio.ViewModels.Models.Session;

/// <summary>The kind of restorable tab (FR-146). Settings/History tabs are transient and not restored.</summary>
public enum SessionTabKind
{
    Invocation,
    Describe
}

/// <summary>One restored metadata header on an invocation draft.</summary>
public sealed record SessionHeader(string Name, string Value, bool IsBin, bool RequiresValue);

/// <summary>
///     One open tab captured for restore (FR-146). For an invocation tab the draft body and options are
///     carried so it reopens exactly as left (run state always idle). For a describe tab only the symbol
///     is needed. <see cref="ConnectionId" /> is resolved against the reopened workspace; a tab whose
///     connection no longer exists is skipped.
/// </summary>
public sealed record SessionTab(
    SessionTabKind Kind,
    string ConnectionId,
    string Symbol,
    string? Body = null,
    RequestBodyFormat BodyFormat = RequestBodyFormat.Json,
    IReadOnlyList<SessionHeader>? Headers = null,
    string? Deadline = null,
    bool EmitDefaults = false,
    bool AllowUnknownFields = true,
    string? MaxMessageSize = null);

/// <summary>
///     The per-machine UI session (SPEC-040 §2; <c>ui-state.json</c>, never the workspace file per FR-141):
///     which tabs were open against which workspace, and which was active. Restored on launch when the
///     FR-151 startup setting is "restore last workspace + tabs".
/// </summary>
public sealed class SessionState
{
    /// <summary>The workspace these tabs belong to; restore is skipped when it differs from the reopened workspace.</summary>
    public string? WorkspaceId { get; set; }

    /// <summary>Index of the active tab among <see cref="Tabs" />, or -1 when none.</summary>
    public int ActiveTabIndex { get; set; } = -1;

    public List<SessionTab> Tabs { get; set; } = [];
}
