using System.Text.Json.Serialization;

namespace GrpCurl.Net.Studio.Services;

/// <summary>
///     Source-generation context for <c>recent-workspaces.json</c> (SPEC-040 §1): a plain JSON array of
///     absolute workspace paths, newest first, capped at ten. Cosmetic data — regenerated empty on any
///     read failure, no toast.
/// </summary>
[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(List<string>))]
internal sealed partial class RecentWorkspacesJsonContext : JsonSerializerContext;
