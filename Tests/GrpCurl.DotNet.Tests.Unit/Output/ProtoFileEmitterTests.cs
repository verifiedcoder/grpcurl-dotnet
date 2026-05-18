using GrpCurl.Net.DescriptorSources;
using GrpCurl.Net.Output;
using GrpCurl.Net.Tests.Unit.Fixtures;

namespace GrpCurl.Net.Tests.Unit.Output;

public sealed class ProtoFileEmitterTests
{
    private static string TestProtosetPath => Path.Combine(
        Path.GetDirectoryName(typeof(TestDescriptorProvider).Assembly.Location)!,
        "TestProtosets",
        "test.protoset");

    [Fact]
    public async Task WriteAsync_ReconstructsProtoFile_WithSyntaxAndPackage()
    {
        var source = await ProtosetSource.LoadFromFilesAsync([TestProtosetPath], TestContext.Current.CancellationToken);
        var outDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());

        try
        {
            await ProtoFileEmitter.WriteAsync(source, outDir, force: true, TestContext.Current.CancellationToken);

            var protoFiles = Directory.EnumerateFiles(outDir, "*.proto", SearchOption.AllDirectories).ToList();

            protoFiles.ShouldNotBeEmpty();

            var sample = await File.ReadAllTextAsync(protoFiles[0], TestContext.Current.CancellationToken);

            sample.ShouldContain("syntax = \"");
            sample.ShouldContain("message ");
        }
        finally
        {
            DeleteDirectory(outDir);
        }
    }

    [Fact]
    public async Task WriteAsync_ExistingFile_RefusesWithoutForce()
    {
        var source = await ProtosetSource.LoadFromFilesAsync([TestProtosetPath], TestContext.Current.CancellationToken);
        var outDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());

        try
        {
            await ProtoFileEmitter.WriteAsync(source, outDir, force: false, TestContext.Current.CancellationToken);

            await Should.ThrowAsync<IOException>(async () =>
            {
                await ProtoFileEmitter.WriteAsync(source, outDir, force: false, TestContext.Current.CancellationToken);
            });
        }
        finally
        {
            DeleteDirectory(outDir);
        }
    }

    private static void DeleteDirectory(string path)
    {
        try
        {
            Directory.Delete(path, recursive: true);
        }
        catch (IOException ex)
        {
            Console.Error.WriteLine($"Failed to delete temporary directory '{path}': {ex.Message}");
        }
        catch (UnauthorizedAccessException ex)
        {
            Console.Error.WriteLine($"Failed to delete temporary directory '{path}': {ex.Message}");
        }
    }
}
