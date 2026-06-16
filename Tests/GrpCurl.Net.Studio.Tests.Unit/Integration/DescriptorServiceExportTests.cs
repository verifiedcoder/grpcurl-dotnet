using GrpCurl.Net.Studio.Services;
using GrpCurl.Net.Studio.ViewModels.Models.Connections;
using GrpCurl.Net.Studio.ViewModels.Models.Descriptors;

namespace GrpCurl.Net.Studio.Tests.Unit.Integration;

/// <summary>
///     L2 schema-export tests through the real <see cref="DescriptorService" /> → Core
///     <c>ProtosetExporter</c> / <c>ProtoFileEmitter</c>. Offline (a protoset-backed connection needs no
///     server): export a protoset that loads back (round-trip), reconstruct .proto files, and verify the
///     refuse-by-default overwrite gate.
/// </summary>
public sealed class DescriptorServiceExportTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "grpcn-export-" + Guid.NewGuid().ToString("N"));

    public DescriptorServiceExportTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    private static string SourceProtoset => Path.Combine(AppContext.BaseDirectory, "TestProtosets", "test.protoset");

    private static SavedConnection ProtosetConnection() => new()
    {
        Name = "src",
        Address = "", // protoset source needs no channel
        Transport = TransportMode.Plaintext,
        DescriptorSource = new DescriptorSourceConfig { Mode = DescriptorMode.Protoset, ProtosetPaths = [SourceProtoset] }
    };

    [Fact]
    public async Task Export_protoset_writes_a_file_that_loads_back()
    {
        var outPath = Path.Combine(_dir, "out.protoset");

        var result = await new DescriptorService().ExportProtosetAsync(ProtosetConnection(), outPath, overwrite: false, TestContext.Current.CancellationToken);

        result.Ok.ShouldBeTrue(result.ErrorMessage);
        result.Written.ShouldHaveSingleItem().SizeBytes.ShouldBeGreaterThan(0);
        File.Exists(outPath).ShouldBeTrue();

        // Round-trip: a connection pointed at the exported protoset lists the same service.
        var roundTrip = new SavedConnection
        {
            Name = "rt", Address = "", Transport = TransportMode.Plaintext,
            DescriptorSource = new DescriptorSourceConfig { Mode = DescriptorMode.Protoset, ProtosetPaths = [outPath] }
        };
        var loaded = await new DescriptorService().LoadAsync(roundTrip, TestContext.Current.CancellationToken);
        loaded.Ok.ShouldBeTrue(loaded.Error?.Message);
        loaded.Catalog!.Services.ShouldContain(s => s.FullName == "testing.TestService");
    }

    [Fact]
    public async Task Reconstruct_protos_writes_proto_files()
    {
        var result = await new DescriptorService().ExportProtosAsync(ProtosetConnection(), _dir, overwrite: false, TestContext.Current.CancellationToken);

        result.Ok.ShouldBeTrue(result.ErrorMessage);
        result.Written.ShouldNotBeEmpty();
        Directory.GetFiles(_dir, "*.proto", SearchOption.AllDirectories).ShouldNotBeEmpty();
    }

    [Fact]
    public async Task Export_refuses_to_overwrite_until_confirmed()
    {
        var outPath = Path.Combine(_dir, "exists.protoset");
        await File.WriteAllTextAsync(outPath, "stale", TestContext.Current.CancellationToken);

        var refused = await new DescriptorService().ExportProtosetAsync(ProtosetConnection(), outPath, overwrite: false, TestContext.Current.CancellationToken);
        refused.Outcome.ShouldBe(SchemaExportOutcome.Conflict);
        refused.Conflicts.ShouldHaveSingleItem().Path.ShouldBe(outPath);

        var forced = await new DescriptorService().ExportProtosetAsync(ProtosetConnection(), outPath, overwrite: true, TestContext.Current.CancellationToken);
        forced.Ok.ShouldBeTrue(forced.ErrorMessage);
        new FileInfo(outPath).Length.ShouldBeGreaterThan(5); // overwrote the 5-byte "stale"
    }
}
