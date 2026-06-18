namespace GrpCurl.Net.Studio.ViewModels.Models.Connections;

/// <summary>
///     The advisory single-writer lock record written beside a workspace file (SPEC-040 §8,
///     <c>&lt;file&gt;.lock</c>): which process on which machine holds it, when it was acquired, and the
///     Studio version. Advisory by design — it guards Studio-vs-Studio, not git or external editors.
/// </summary>
public sealed record WorkspaceLockInfo(int Pid, string Machine, DateTimeOffset AcquiredUtc, string AppVersion);
