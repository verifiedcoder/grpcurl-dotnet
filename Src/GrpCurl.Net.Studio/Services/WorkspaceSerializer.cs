using System.Text.Json;
using GrpCurl.Net.Studio.ViewModels.Models.Connections;
using GrpCurl.Net.Studio.ViewModels.Services;

namespace GrpCurl.Net.Studio.Services;

/// <summary>
///     Serializes and deserializes the versioned workspace document (SPEC-040 §3). Output is camelCase,
///     indented, UTF-8 with no BOM and <c>\n</c> line endings regardless of OS, for stable git diffs and
///     byte-stable golden files. Loading enforces the version policy: reject a newer schema whole-file
///     (<see cref="WorkspaceSchemaException" />), upgrade an older one in memory via the migration chain,
///     and backfill identity for legacy files that predate the <c>id</c>/<c>name</c> fields.
/// </summary>
internal static class WorkspaceSerializer
{
    /// <summary>Serializes to the canonical on-disk text (LF line endings).</summary>
    public static string Serialize(WorkspaceModel workspace)
        => JsonSerializer.Serialize(workspace, WorkspaceJsonContext.Default.WorkspaceModel).ReplaceLineEndings("\n");

    /// <summary>UTF-8 (no BOM) bytes of <see cref="Serialize" />, ready for an atomic write.</summary>
    public static byte[] SerializeToUtf8(WorkspaceModel workspace)
        => System.Text.Encoding.UTF8.GetBytes(Serialize(workspace));

    /// <summary>
    ///     Parses a workspace document, applying the version policy. Throws
    ///     <see cref="WorkspaceSchemaException" /> for a newer or corrupt file.
    /// </summary>
    public static WorkspaceModel Deserialize(string json)
    {
        int version;

        try
        {
            using var document = JsonDocument.Parse(json);
            version = document.RootElement.ValueKind == JsonValueKind.Object
                      && document.RootElement.TryGetProperty("schemaVersion", out var v)
                      && v.TryGetInt32(out var parsed)
                ? parsed
                : WorkspaceModel.CurrentSchemaVersion; // legacy files predate the field; treat as current
        }
        catch (JsonException ex)
        {
            throw WorkspaceSchemaException.Corrupt(ex.Message);
        }

        if (version > WorkspaceModel.CurrentSchemaVersion)
        {
            throw WorkspaceSchemaException.NewerVersion(version, WorkspaceModel.CurrentSchemaVersion);
        }

        // Migration chain: as schema versions are added, run MigrateVnToVn+1(JsonNode) here until current.
        // v1 is the baseline, so there is nothing to migrate yet.

        WorkspaceModel model;

        try
        {
            model = JsonSerializer.Deserialize(json, WorkspaceJsonContext.Default.WorkspaceModel)
                    ?? throw WorkspaceSchemaException.Corrupt("the document was empty");
        }
        catch (JsonException ex)
        {
            throw WorkspaceSchemaException.Corrupt(ex.Message);
        }

        Backfill(model);
        return model;
    }

    /// <summary>Ensures a loaded model has the current version and a non-empty identity/name.</summary>
    private static void Backfill(WorkspaceModel model)
    {
        if (string.IsNullOrWhiteSpace(model.Id))
        {
            model.Id = Guid.NewGuid().ToString("D");
        }

        if (string.IsNullOrWhiteSpace(model.Name))
        {
            model.Name = "Default";
        }

        model.SchemaVersion = WorkspaceModel.CurrentSchemaVersion;
    }
}
