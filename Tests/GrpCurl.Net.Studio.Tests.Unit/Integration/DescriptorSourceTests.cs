using GrpCurl.Net.Studio.Services;
using GrpCurl.Net.Studio.Tests.Unit.Fixtures;
using GrpCurl.Net.Studio.ViewModels.Models.Connections;
using GrpCurl.Net.Studio.ViewModels.Models.Invocation;
using GrpCurl.Net.Studio.ViewModels.Services;

namespace GrpCurl.Net.Studio.Tests.Unit.Integration;

/// <summary>
///     L2 service-layer E2E for the E2.3 protoset and proto descriptor sources: a connection configured
///     with a pre-built protoset (or a <c>.proto</c> compiled by <c>protoc</c>) lists/describes/invokes
///     through the real Studio services → Core → the in-process TestServer, with the schema coming from
///     the local source rather than reflection.
/// </summary>
[Collection(StudioPlaintextServerCollection.Name)]
public sealed class DescriptorSourceTests(StudioPlaintextServerFixture server)
{
    private static string ProtosetPath => Path.Combine(AppContext.BaseDirectory, "TestProtosets", "test.protoset");

    private static string ProtoPath => Path.Combine(AppContext.BaseDirectory, "Protos", "test.proto");

    // Proto-source tests compile with the external protoc; skip where it's absent (e.g. CI runners
    // without protobuf-compiler), matching the CLI's ListDescribeProtoOptionTests.
    private static bool ProtocOnPath()
    {
        var executable = OperatingSystem.IsWindows() ? "protoc.exe" : "protoc";
        var pathVar = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;

        return pathVar
            .Split(Path.PathSeparator)
            .Where(dir => !string.IsNullOrWhiteSpace(dir))
            .Any(dir => File.Exists(Path.Combine(dir, executable)));
    }

    private SavedConnection Protoset() => new()
    {
        Name = "protoset",
        Address = server.Address,
        Transport = TransportMode.Plaintext,
        DescriptorSource = new DescriptorSourceConfig { Mode = DescriptorMode.Protoset, ProtosetPaths = [ProtosetPath] }
    };

    private SavedConnection Proto() => new()
    {
        Name = "proto",
        Address = server.Address,
        Transport = TransportMode.Plaintext,
        DescriptorSource = new DescriptorSourceConfig
        {
            Mode = DescriptorMode.Proto,
            ProtoFiles = [ProtoPath],
            ImportPaths = [Path.GetDirectoryName(ProtoPath)!]
        }
    };

    [Fact]
    public async Task Protoset_source_lists_services_without_reflection()
    {
        var result = await new DescriptorService().LoadAsync(Protoset(), TestContext.Current.CancellationToken);

        result.Ok.ShouldBeTrue(result.Error?.Message);
        result.Catalog!.Services.ShouldContain(s => s.FullName == "testing.TestService");
    }

    [Fact]
    public async Task Protoset_source_describes_a_symbol()
    {
        var result = await new DescriptorService().DescribeAsync(Protoset(), "testing.TestService", TestContext.Current.CancellationToken);

        result.Ok.ShouldBeTrue(result.Error?.Message);
    }

    [Fact]
    public async Task Protoset_source_invokes_against_the_server()
    {
        var runner = new InvocationRunner(new InvocationService());
        var request = new InvocationRequestModel(Protoset(), "testing.TestService/EmptyCall", "{}", []);

        var result = await runner.InvokeUnaryAsync(request, TestContext.Current.CancellationToken);

        result.Ok.ShouldBeTrue(result.ErrorMessage);
        result.Status.Code.ShouldBe(0);
    }

    [Fact]
    public async Task Proto_source_compiles_and_lists_services()
    {
        Assert.SkipUnless(ProtocOnPath(), "protoc is not installed on PATH");

        var result = await new DescriptorService().LoadAsync(Proto(), TestContext.Current.CancellationToken);

        result.Ok.ShouldBeTrue(result.Error?.Message);
        result.Catalog!.Services.ShouldContain(s => s.FullName == "testing.TestService");
    }

    [Fact]
    public async Task Proto_source_invokes_against_the_server()
    {
        Assert.SkipUnless(ProtocOnPath(), "protoc is not installed on PATH");

        var runner = new InvocationRunner(new InvocationService());
        var request = new InvocationRequestModel(Proto(), "testing.TestService/EmptyCall", "{}", []);

        var result = await runner.InvokeUnaryAsync(request, TestContext.Current.CancellationToken);

        result.Ok.ShouldBeTrue(result.ErrorMessage);
    }

    [Fact]
    public async Task A_missing_protoset_file_surfaces_a_schema_error()
    {
        var connection = new SavedConnection
        {
            Name = "bad",
            Address = server.Address,
            Transport = TransportMode.Plaintext,
            DescriptorSource = new DescriptorSourceConfig
            {
                Mode = DescriptorMode.Protoset,
                ProtosetPaths = [Path.Combine(AppContext.BaseDirectory, "TestProtosets", "does-not-exist.protoset")]
            }
        };

        var result = await new DescriptorService().LoadAsync(connection, TestContext.Current.CancellationToken);

        result.Ok.ShouldBeFalse();
        _ = result.Error.ShouldNotBeNull();
    }
}
