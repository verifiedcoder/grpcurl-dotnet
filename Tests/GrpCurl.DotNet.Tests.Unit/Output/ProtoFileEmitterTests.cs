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
        // Arrange
        var source = await ProtosetSource.LoadFromFilesAsync([TestProtosetPath], TestContext.Current.CancellationToken);
        var outDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());

        try
        {
            // Act
            await ProtoFileEmitter.WriteAsync(source, outDir, force: true, TestContext.Current.CancellationToken);

            // Assert
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
        // Arrange
        var source = await ProtosetSource.LoadFromFilesAsync([TestProtosetPath], TestContext.Current.CancellationToken);
        var outDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());

        try
        {
            // Act
            await ProtoFileEmitter.WriteAsync(source, outDir, force: false, TestContext.Current.CancellationToken);

            // Assert
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

    [Fact]
    public void ResolveContainedPath_SafeNestedDescriptorName_ReturnsPathUnderOutputDirectory()
    {
        // Arrange
        var outDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        var expected = Path.GetFullPath(Path.Combine(outDir, "pkg", "test.proto"));

        // Act
        var result = ProtoFileEmitter.ResolveContainedPath(outDir, "pkg/test.proto");

        // Assert
        result.ShouldBe(expected);
    }

    [Theory]
    [InlineData("../escape.proto")]
    [InlineData("pkg/../../escape.proto")]
    [InlineData("/tmp/escape.proto")]
    [InlineData("C:\\temp\\escape.proto")]
    [InlineData("\\\\server\\share\\escape.proto")]
    public void ResolveContainedPath_UnsafeDescriptorName_ThrowsInvalidDataException(string descriptorName)
    {
        // Arrange
        var outDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());

        // Act
        var exception = Should.Throw<InvalidDataException>(() =>
            ProtoFileEmitter.ResolveContainedPath(outDir, descriptorName));

        // Assert
        exception.Message.ShouldContain("Descriptor file name");
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
