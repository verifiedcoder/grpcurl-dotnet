using System.Text.Json;
using System.Text.Json.Serialization;

namespace GrpCurl.Net.Studio.ViewModels.Models.Connections;

/// <summary>
///     The persisted workspace document (SPEC-040 §3.2): a self-contained <c>.gcnws.json</c> file
///     identified by a stable <see cref="Id" /> (which namespaces its secret keyrefs) and a display
///     <see cref="Name" />. <see cref="SchemaVersion" /> starts at 1; migrations upgrade older files
///     in memory on load (SPEC-040 §"versioning"). Fields a newer Studio adds within the same schema
///     version are preserved on round-trip via <see cref="Overflow" /> (additive-only rule).
/// </summary>
public sealed class WorkspaceModel
{
    /// <summary>The newest workspace schema this build can read and write.</summary>
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;

    /// <summary>Stable workspace identity (UUID); namespaces secret keyrefs. Backfilled if a legacy file lacks it.</summary>
    public string Id { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public List<SavedConnection> Connections { get; set; } = [];

    /// <summary>Named TLS bundles referenced by connections (FR-030; workspace-level per SPEC-010 §1.2).</summary>
    public List<TlsProfile> TlsProfiles { get; set; } = [];

    /// <summary>
    ///     Forward-compatibility bag: properties present in the file but not modelled by this build
    ///     (e.g. <c>savedRequests</c>/<c>environments</c> added by a newer Studio at the same schema
    ///     version) are kept here and re-emitted on save, so an older Studio never silently drops them.
    /// </summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Overflow { get; set; }

    /// <summary>A fresh, empty workspace with a new identity and a default name.</summary>
    public static WorkspaceModel Empty() => new()
    {
        Id = Guid.NewGuid().ToString("D"),
        Name = "Default"
    };

    /// <summary>
    ///     A copy preserving identity (id/name/schemaVersion/overflow) with fresh collection instances.
    ///     Mutators clone the live workspace and replace just the collection they own, so a connection-list
    ///     save never wipes the workspace id or another section's data.
    /// </summary>
    public WorkspaceModel Copy() => new()
    {
        SchemaVersion = SchemaVersion,
        Id = Id,
        Name = Name,
        Connections = [.. Connections],
        TlsProfiles = [.. TlsProfiles],
        Overflow = Overflow is null ? null : new Dictionary<string, JsonElement>(Overflow)
    };
}
