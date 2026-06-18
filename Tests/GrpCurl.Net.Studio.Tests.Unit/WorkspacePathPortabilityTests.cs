using GrpCurl.Net.Studio.Services;
using GrpCurl.Net.Studio.ViewModels.Models.Connections;

namespace GrpCurl.Net.Studio.Tests.Unit;

/// <summary>FR-147: file references are stored relative to the workspace dir when beneath it, absolute otherwise.</summary>
public sealed class WorkspacePathPortabilityTests
{
    private static readonly string Base = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "ws-portability"));

    [Fact]
    public void A_path_beneath_the_workspace_dir_relativises_with_forward_slashes()
    {
        var abs = Path.Combine(Base, "certs", "server.pem");

        WorkspacePathPortability.MakeRelative(abs, Base).ShouldBe("certs/server.pem");
    }

    [Fact]
    public void A_path_outside_the_workspace_dir_stays_absolute()
    {
        var outside = Path.GetFullPath(Path.Combine(Base, "..", "elsewhere", "ca.pem"));

        WorkspacePathPortability.MakeRelative(outside, Base).ShouldBe(outside);
    }

    [Fact]
    public void An_already_relative_path_is_left_untouched_by_relativise()
        => WorkspacePathPortability.MakeRelative("certs/server.pem", Base).ShouldBe("certs/server.pem");

    [Fact]
    public void Resolve_makes_a_relative_path_absolute_and_leaves_absolute_alone()
    {
        var resolved = WorkspacePathPortability.MakeAbsolute("certs/server.pem", Base);
        resolved.ShouldBe(Path.Combine(Base, "certs", "server.pem"));

        var already = Path.Combine(Base, "x.pem");
        WorkspacePathPortability.MakeAbsolute(already, Base).ShouldBe(already);
    }

    [Fact]
    public void Round_trip_through_relative_and_back_preserves_a_beneath_path()
    {
        var abs = Path.Combine(Base, "protos", "svc.protoset");

        var relative = WorkspacePathPortability.MakeRelative(abs, Base);
        WorkspacePathPortability.MakeAbsolute(relative!, Base).ShouldBe(abs);
    }

    [Fact]
    public void ToRelative_does_not_mutate_the_live_model()
    {
        var abs = Path.Combine(Base, "protos", "svc.protoset");
        var workspace = new WorkspaceModel
        {
            Connections = [new SavedConnection { Name = "c", Address = "h:1", DescriptorSource = new DescriptorSourceConfig { ProtosetPaths = [abs] } }],
            TlsProfiles = [new TlsProfile { Name = "p", CaCertPath = Path.Combine(Base, "ca.pem") }]
        };

        var portable = WorkspacePathPortability.ToRelative(workspace, Base);

        portable.Connections[0].DescriptorSource.ProtosetPaths[0].ShouldBe("protos/svc.protoset");
        portable.TlsProfiles[0].CaCertPath.ShouldBe("ca.pem");
        workspace.Connections[0].DescriptorSource.ProtosetPaths[0].ShouldBe(abs); // live model untouched
        portable.Connections[0].Id.ShouldBe(workspace.Connections[0].Id); // identity preserved
    }
}
