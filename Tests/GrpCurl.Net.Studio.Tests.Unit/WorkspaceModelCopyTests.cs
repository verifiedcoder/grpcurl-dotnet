using GrpCurl.Net.Studio.ViewModels.Models.Connections;
using System.Text.Json;

namespace GrpCurl.Net.Studio.Tests.Unit;

/// <summary>
///     E3.1 PR-C: WorkspaceModel.Copy preserves the workspace identity (id/name/schemaVersion/overflow)
///     while giving fresh collections, so a section-scoped save never wipes another section or the id.
/// </summary>
public sealed class WorkspaceModelCopyTests
{
    [Fact]
    public void Copy_preserves_identity_and_overflow()
    {
        var original = new WorkspaceModel
        {
            SchemaVersion = 1,
            Id = "11111111-1111-1111-1111-111111111111",
            Name = "Prod",
            Connections = [new SavedConnection { Name = "a", Address = "h:1" }],
            TlsProfiles = [new TlsProfile { Name = "mtls" }],
            Overflow = new Dictionary<string, JsonElement>
            {
                ["savedRequests"] = JsonDocument.Parse("[ { \"id\": \"r1\" } ]").RootElement.Clone()
            }
        };

        var copy = original.Copy();

        copy.Id.ShouldBe(original.Id);
        copy.Name.ShouldBe("Prod");
        copy.SchemaVersion.ShouldBe(1);
        copy.Overflow.ShouldNotBeNull().ShouldContainKey("savedRequests");
    }

    [Fact]
    public void Copy_isolates_the_collections_from_the_original()
    {
        var original = new WorkspaceModel { Id = "x", Name = "n", Connections = [new SavedConnection { Name = "a", Address = "h:1" }] };

        var copy = original.Copy();
        copy.Connections.Add(new SavedConnection { Name = "b", Address = "h:2" });

        original.Connections.Count.ShouldBe(1); // mutating the copy's list does not touch the original
        copy.Connections.Count.ShouldBe(2);
    }
}
