using GrpCurl.Net.Studio.Services;
using GrpCurl.Net.Studio.ViewModels.Models.Connections;
using System.Text.Json;

namespace GrpCurl.Net.Studio.Tests.Unit;

/// <summary>
///     L1 tests for the E2.3 descriptor-source model: deep clone, connection round-trip, the
///     <see cref="ConnectionChannelMapper.DescriptorPaths" /> mode gating, and JSON persistence
///     (including back-compat for a workspace written before the field existed).
/// </summary>
public sealed class DescriptorSourceConfigTests
{
    [Fact]
    public void Clone_deep_copies_the_path_lists()
    {
        var config = new DescriptorSourceConfig
        {
            Mode = DescriptorMode.Proto,
            ProtoFiles = ["a.proto"],
            ImportPaths = ["/inc"]
        };

        var copy = config.Clone();
        copy.ProtoFiles.Add("b.proto");

        _ = config.ProtoFiles.ShouldHaveSingleItem(); // original untouched
        copy.Mode.ShouldBe(DescriptorMode.Proto);
        copy.ImportPaths.ShouldBe(["/inc"]);
    }

    [Fact]
    public void Saved_connection_clone_copies_the_descriptor_source()
    {
        var connection = new SavedConnection
        {
            Name = "c",
            DescriptorSource = new DescriptorSourceConfig { Mode = DescriptorMode.Protoset, ProtosetPaths = ["x.protoset"] }
        };

        var clone = connection.Clone();
        clone.DescriptorSource.Mode.ShouldBe(DescriptorMode.Protoset);
        clone.DescriptorSource.ProtosetPaths.ShouldBe(["x.protoset"]);

        clone.DescriptorSource.ProtosetPaths.Add("y.protoset");
        _ = connection.DescriptorSource.ProtosetPaths.ShouldHaveSingleItem(); // independent
    }

    [Fact]
    public void Descriptor_paths_passes_only_the_active_modes_lists()
    {
        var protoset = new SavedConnection
        {
            DescriptorSource = new DescriptorSourceConfig
            {
                Mode = DescriptorMode.Protoset,
                ProtosetPaths = ["a.protoset"],
                ProtoFiles = ["stale.proto"] // lingering from a previous mode — must be ignored
            }
        };

        var (protosets, protos, imports) = ConnectionChannelMapper.DescriptorPaths(protoset);
        protosets.ShouldBe(["a.protoset"]);
        protos.ShouldBeEmpty();
        imports.ShouldBeEmpty();

        var proto = new SavedConnection
        {
            DescriptorSource = new DescriptorSourceConfig
            {
                Mode = DescriptorMode.Proto,
                ProtoFiles = ["a.proto"],
                ImportPaths = ["/inc"]
            }
        };

        var (ps2, pf2, imp2) = ConnectionChannelMapper.DescriptorPaths(proto);
        ps2.ShouldBeEmpty();
        pf2.ShouldBe(["a.proto"]);
        imp2.ShouldBe(["/inc"]);
    }

    [Fact]
    public void Reflection_mode_passes_no_paths()
    {
        var (protosets, protos, imports) = ConnectionChannelMapper.DescriptorPaths(new SavedConnection());

        protosets.ShouldBeEmpty();
        protos.ShouldBeEmpty();
        imports.ShouldBeEmpty();
    }

    [Fact]
    public void Descriptor_source_round_trips_through_workspace_json()
    {
        var workspace = new WorkspaceModel
        {
            Connections =
            [
                new SavedConnection
                {
                    Name = "proto-conn",
                    DescriptorSource = new DescriptorSourceConfig
                    {
                        Mode = DescriptorMode.Proto,
                        ProtoFiles = ["svc.proto"],
                        ImportPaths = ["/inc"]
                    }
                }
            ]
        };

        var json = JsonSerializer.Serialize(workspace, WorkspaceJsonContext.Default.WorkspaceModel);
        json.ShouldContain("\"mode\": \"proto\""); // camelCase enum
        json.ShouldContain("svc.proto");

        var restored = JsonSerializer.Deserialize(json, WorkspaceJsonContext.Default.WorkspaceModel)!;
        var source = restored.Connections.Single().DescriptorSource;
        source.Mode.ShouldBe(DescriptorMode.Proto);
        source.ProtoFiles.ShouldBe(["svc.proto"]);
        source.ImportPaths.ShouldBe(["/inc"]);
    }

    [Fact]
    public void A_workspace_without_a_descriptor_source_defaults_to_reflection()
    {
        // A pre-E2.3 connection JSON had no descriptorSource field.
        const string legacy = """
        { "schemaVersion": 1, "connections": [ { "id": "x", "name": "old", "address": "a:443" } ] }
        """;

        var restored = JsonSerializer.Deserialize(legacy, WorkspaceJsonContext.Default.WorkspaceModel)!;
        var source = restored.Connections.Single().DescriptorSource;

        _ = source.ShouldNotBeNull();
        source.Mode.ShouldBe(DescriptorMode.Reflection);
        source.ProtosetPaths.ShouldBeEmpty();
    }
}
