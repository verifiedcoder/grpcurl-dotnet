using System.Text;
using GrpCurl.Net.Studio.Services;
using GrpCurl.Net.Studio.ViewModels.Models.Connections;
using GrpCurl.Net.Studio.ViewModels.Services;

namespace GrpCurl.Net.Studio.Tests.Unit;

/// <summary>
///     E3.1: the versioned workspace serializer (SPEC-040 §3) — canonical format (LF, no BOM, camelCase,
///     indented), version policy (reject newer, backfill legacy), forward-field preservation, and a
///     committed v1 golden that catches accidental format drift (SPEC-070 §4).
/// </summary>
public sealed class WorkspaceSerializerTests
{
    private static WorkspaceModel Sample() => new()
    {
        SchemaVersion = 1,
        Id = "0b3c9f6e-1a2b-4c3d-8e4f-5a6b7c8d9e0f",
        Name = "Sample workspace",
        Connections = [],
        TlsProfiles = []
    };

    private static string GoldenPath()
        => Path.Combine(AppContext.BaseDirectory, "Goldens", "workspace.v1.gcnws.json");

    [Fact]
    public void Serializes_with_lf_endings_and_no_bom()
    {
        var bytes = WorkspaceSerializer.SerializeToUtf8(Sample());

        var hasBom = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF;
        hasBom.ShouldBeFalse();

        var text = Encoding.UTF8.GetString(bytes);
        text.ShouldNotContain("\r");                 // LF only, regardless of OS
        text.ShouldContain("\"schemaVersion\": 1");  // camelCase, indented
    }

    [Fact]
    public void Round_trips_byte_stable()
    {
        var json = WorkspaceSerializer.Serialize(Sample());

        var model = WorkspaceSerializer.Deserialize(json);

        WorkspaceSerializer.Serialize(model).ShouldBe(json);
    }

    [Fact]
    public void Current_serialization_matches_the_committed_v1_golden()
    {
        var golden = File.ReadAllText(GoldenPath()).ReplaceLineEndings("\n");

        WorkspaceSerializer.Serialize(Sample()).ShouldBe(golden);
    }

    [Fact]
    public void The_v1_golden_round_trips_byte_stable()
    {
        var golden = File.ReadAllText(GoldenPath()).ReplaceLineEndings("\n");

        var model = WorkspaceSerializer.Deserialize(golden);

        WorkspaceSerializer.Serialize(model).ShouldBe(golden);
    }

    [Fact]
    public void A_newer_schema_version_is_rejected_whole_file()
    {
        var json = """{ "schemaVersion": 999, "id": "0b3c9f6e-1a2b-4c3d-8e4f-5a6b7c8d9e0f", "name": "future" }""";

        var ex = Should.Throw<WorkspaceSchemaException>(() => WorkspaceSerializer.Deserialize(json));

        ex.IsNewerVersion.ShouldBeTrue();
        ex.Message.ShouldContain("newer version of Studio");
    }

    [Fact]
    public void Corrupt_json_throws_a_friendly_non_version_error()
    {
        var ex = Should.Throw<WorkspaceSchemaException>(() => WorkspaceSerializer.Deserialize("{ not json"));

        ex.IsNewerVersion.ShouldBeFalse();
        ex.Message.ShouldContain("not valid");
    }

    [Fact]
    public void A_legacy_file_without_id_or_name_is_backfilled()
    {
        var json = """{ "schemaVersion": 1, "connections": [], "tlsProfiles": [] }""";

        var model = WorkspaceSerializer.Deserialize(json);

        model.Id.ShouldNotBeNullOrWhiteSpace();
        model.Name.ShouldBe("Default");
        model.SchemaVersion.ShouldBe(WorkspaceModel.CurrentSchemaVersion);
    }

    [Fact]
    public void Unknown_forward_fields_survive_a_round_trip()
    {
        // A v1 file written by a newer Studio with an additive field this build doesn't model
        // (graphqlRequests is in the schema but not yet modelled here — exactly the forward-compat case).
        var json = """{ "schemaVersion": 1, "id": "abc", "name": "n", "graphqlRequests": [ { "id": "g1" } ] }""";

        var reserialized = WorkspaceSerializer.Serialize(WorkspaceSerializer.Deserialize(json));

        reserialized.ShouldContain("graphqlRequests");
        reserialized.ShouldContain("g1");
    }
}
