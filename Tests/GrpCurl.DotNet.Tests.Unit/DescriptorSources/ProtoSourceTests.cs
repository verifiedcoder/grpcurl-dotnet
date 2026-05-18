using Google.Protobuf.Reflection;
using GrpCurl.Net.DescriptorSources;

namespace GrpCurl.Net.Tests.Unit.DescriptorSources;

/// <summary>
///     Exercises the <see cref="ProtoSource"/> descriptor source by shelling out to
///     <c>protoc</c>. The whole suite is skipped when <c>protoc</c> isn't on PATH so
///     contributors without it installed don't see a hard failure.
/// </summary>
public sealed class ProtoSourceTests
{
    private static bool ProtocAvailable
    {
        get
        {
            var pathVar = Environment.GetEnvironmentVariable("PATH");

            if (string.IsNullOrEmpty(pathVar))
            {
                return false;
            }

            var executable = OperatingSystem.IsWindows() ? "protoc.exe" : "protoc";

            return pathVar.Split(Path.PathSeparator)
                .Any(dir =>
                {
                    try { return File.Exists(Path.Combine(dir, executable)); }
                    catch (ArgumentException) { return false; }
                });
        }
    }

    private static string TestProtoPath
    {
        get
        {
            var probe = new[]
            {
                Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "GrpCurl.Net.TestServer", "Protos", "test.proto"),
                Path.Combine(Environment.CurrentDirectory, "Tests", "GrpCurl.Net.TestServer", "Protos", "test.proto"),
                Path.Combine(Environment.CurrentDirectory, "..", "GrpCurl.Net.TestServer", "Protos", "test.proto")
            };

            foreach (var candidate in probe)
            {
                var full = Path.GetFullPath(candidate);

                if (File.Exists(full))
                {
                    return full;
                }
            }

            throw new FileNotFoundException("Could not locate Tests/GrpCurl.Net.TestServer/Protos/test.proto");
        }
    }

    [Fact]
    public async Task LoadFromProtoFilesAsync_ResolvesService_FromProtoFile()
    {
        if (!ProtocAvailable)
        {
            return;
        }

        var protoPath = TestProtoPath;
        var importRoot = Path.GetDirectoryName(protoPath)!;

        var source = await ProtoSource.LoadFromProtoFilesAsync(
            [protoPath],
            [importRoot],
            TestContext.Current.CancellationToken);

        var symbol = await source.FindSymbolAsync("testing.TestService", TestContext.Current.CancellationToken);

        symbol.ShouldBeOfType<ServiceDescriptor>();
        ((ServiceDescriptor)symbol!).FullName.ShouldBe("testing.TestService");
    }

    [Fact]
    public async Task LoadFromProtoFilesAsync_MissingFile_ThrowsWithProtocStderr()
    {
        if (!ProtocAvailable)
        {
            return;
        }

        var ex = await Should.ThrowAsync<InvalidOperationException>(async () =>
        {
            await ProtoSource.LoadFromProtoFilesAsync(
                ["does-not-exist.proto"],
                [Environment.CurrentDirectory],
                TestContext.Current.CancellationToken);
        });

        ex.Message.ShouldContain("protoc failed");
    }

    [Fact]
    public async Task LoadFromProtoFilesAsync_EmptyFiles_Throws()
    {
        await Should.ThrowAsync<ArgumentException>(async () =>
        {
            await ProtoSource.LoadFromProtoFilesAsync(
                [],
                [],
                TestContext.Current.CancellationToken);
        });
    }
}
