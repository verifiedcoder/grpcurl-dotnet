using GrpCurl.Net.Studio.Services;
using GrpCurl.Net.Studio.ViewModels.Models.Connections;

namespace GrpCurl.Net.Studio.Tests.Unit;

public sealed class WorkspaceReferenceBaselineTests
{
    [Theory]
    [InlineData("protoset")]
    [InlineData("proto")]
    [InlineData("imports")]
    [InlineData("ca")]
    [InlineData("cert")]
    [InlineData("key")]
    public async Task Opening_a_traversing_reference_requires_approval(string field)
    {
        var directory = Path.Combine(Path.GetTempPath(), "workspace-reference-" + Guid.NewGuid().ToString("N"));
        _ = Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "shared.json");
        var connection = new SavedConnection();
        var profile = new TlsProfile();
        const string escape = "../outside/missing";
        switch (field)
        {
            case "protoset": connection.DescriptorSource.ProtosetPaths.Add(escape); break;
            case "proto": connection.DescriptorSource.ProtoFiles.Add(escape); break;
            case "imports": connection.DescriptorSource.ImportPaths.Add(escape); break;
            case "ca": profile.CaCertPath = escape; break;
            case "cert": profile.ClientCertPath = escape; break;
            case "key": profile.ClientKeyPath = escape; break;
        }
        var workspace = new WorkspaceModel { Connections = [connection], TlsProfiles = [profile] };
        await File.WriteAllBytesAsync(path, WorkspaceSerializer.SerializeToUtf8(workspace));
        var store = new JsonWorkspaceStore(Path.Combine(directory, "active.json"));
        try
        {
            _ = await Should.ThrowAsync<InvalidOperationException>(() => store.OpenAsync(path));
            store.Current.Connections.ShouldBeEmpty();
            File.Exists(path + ".corrupt").ShouldBeFalse();
        }
        finally
        {
            store.ReleaseLock();
            Directory.Delete(directory, recursive: true);
        }
    }
}
