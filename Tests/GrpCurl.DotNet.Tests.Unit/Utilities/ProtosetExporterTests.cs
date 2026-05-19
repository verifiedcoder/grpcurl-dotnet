using Google.Protobuf.Reflection;
using GrpCurl.Net.DescriptorSources;
using GrpCurl.Net.Utilities;

namespace GrpCurl.Net.Tests.Unit.Utilities;

public sealed class ProtosetExporterTests : IDisposable
{
    private readonly string _testProtosetPath;
    private readonly string _wellKnownTypesProtosetPath;
    private readonly List<string> _tempFiles = [];

    public ProtosetExporterTests()
    {
        var outputDir = Path.GetDirectoryName(typeof(ProtosetExporterTests).Assembly.Location);

        _testProtosetPath = Path.Combine(outputDir!, "TestProtosets", "test.protoset");
        _wellKnownTypesProtosetPath = Path.Combine(outputDir!, "TestProtosets", "well-known-types.protoset");
    }

    #region WriteProtosetAsync - Null/Empty Path Tests

    [Fact]
    public async Task WriteProtosetAsync_NullPath_DoesNothing()
    {
        // Arrange
        var source = await ProtosetSource.LoadFromFileAsync(_testProtosetPath, TestContext.Current.CancellationToken);

        // Act
        // Assert
        await Should.NotThrowAsync(() =>
            ProtosetExporter.WriteProtosetAsync(source, null));
    }

    [Fact]
    public async Task WriteProtosetAsync_EmptyPath_DoesNothing()
    {
        // Arrange
        var source = await ProtosetSource.LoadFromFileAsync(_testProtosetPath, TestContext.Current.CancellationToken);

        // Act
        // Assert
        await Should.NotThrowAsync(() =>
            ProtosetExporter.WriteProtosetAsync(source, ""));
    }

    #endregion

    #region WriteProtosetAsync - Valid Path Tests

    [Fact]
    public async Task WriteProtosetAsync_ValidPathNoSymbols_WritesFile()
    {
        // Arrange
        var source = await ProtosetSource.LoadFromFileAsync(_testProtosetPath, TestContext.Current.CancellationToken);
        var outputPath = CreateTempFilePath();

        // Act
        await ProtosetExporter.WriteProtosetAsync(source, outputPath);

        // Assert
        File.Exists(outputPath).ShouldBeTrue();

        var fileBytes = await File.ReadAllBytesAsync(outputPath, TestContext.Current.CancellationToken);

        fileBytes.Length.ShouldBeGreaterThan(0);
    }

    [Fact]
    public async Task WriteProtosetAsync_ValidPathNoSymbols_WritesValidProtobuf()
    {
        // Arrange
        var source = await ProtosetSource.LoadFromFileAsync(_testProtosetPath, TestContext.Current.CancellationToken);
        var outputPath = CreateTempFilePath();

        // Act
        await ProtosetExporter.WriteProtosetAsync(source, outputPath);

        // Assert - the output file should be a valid FileDescriptorSet
        var fileBytes = await File.ReadAllBytesAsync(outputPath, TestContext.Current.CancellationToken);
        var parsedFds = FileDescriptorSet.Parser.ParseFrom(fileBytes);

        parsedFds.ShouldNotBeNull();
        parsedFds.File.ShouldNotBeEmpty();
    }

    [Fact]
    public async Task WriteProtosetAsync_WithServiceSymbol_WritesFile()
    {
        // Arrange
        var source = await ProtosetSource.LoadFromFileAsync(_testProtosetPath, TestContext.Current.CancellationToken);
        var outputPath = CreateTempFilePath();

        // Act
        await ProtosetExporter.WriteProtosetAsync(source, outputPath, "testing.TestService");

        // Assert
        File.Exists(outputPath).ShouldBeTrue();

        var fileBytes = await File.ReadAllBytesAsync(outputPath, TestContext.Current.CancellationToken);

        fileBytes.Length.ShouldBeGreaterThan(0);

        // Verify the output is a valid protobuf FileDescriptorSet
        var parsedFds = FileDescriptorSet.Parser.ParseFrom(fileBytes);

        parsedFds.ShouldNotBeNull();
        parsedFds.File.ShouldNotBeEmpty();
    }

    [Fact]
    public async Task WriteProtosetAsync_WithMessageSymbol_WritesFile()
    {
        // Arrange
        var source = await ProtosetSource.LoadFromFileAsync(_testProtosetPath, TestContext.Current.CancellationToken);
        var outputPath = CreateTempFilePath();

        // Act
        await ProtosetExporter.WriteProtosetAsync(source, outputPath, "testing.SimpleRequest");

        // Assert
        File.Exists(outputPath).ShouldBeTrue();

        var fileBytes = await File.ReadAllBytesAsync(outputPath, TestContext.Current.CancellationToken);
        var parsedFds = FileDescriptorSet.Parser.ParseFrom(fileBytes);

        parsedFds.ShouldNotBeNull();
        parsedFds.File.ShouldNotBeEmpty();
    }

    [Fact]
    public async Task WriteProtosetAsync_WithMultipleSymbols_WritesFile()
    {
        // Arrange
        var source = await ProtosetSource.LoadFromFileAsync(_testProtosetPath, TestContext.Current.CancellationToken);
        var outputPath = CreateTempFilePath();

        // Act
        await ProtosetExporter.WriteProtosetAsync(
            source, outputPath,
            "testing.TestService", "testing.SimpleRequest");

        // Assert
        File.Exists(outputPath).ShouldBeTrue();

        var fileBytes = await File.ReadAllBytesAsync(outputPath, TestContext.Current.CancellationToken);
        var parsedFds = FileDescriptorSet.Parser.ParseFrom(fileBytes);

        parsedFds.ShouldNotBeNull();
        parsedFds.File.ShouldNotBeEmpty();
    }

    [Fact]
    public async Task WriteProtosetAsync_WithNonExistentSymbol_WritesEmptySet()
    {
        // Arrange
        var source = await ProtosetSource.LoadFromFileAsync(_testProtosetPath, TestContext.Current.CancellationToken);
        var outputPath = CreateTempFilePath();

        // Act - symbol does not exist, so BuildFileDescriptorSetAsync returns empty FDS
        await ProtosetExporter.WriteProtosetAsync(
            source, outputPath, "nonexistent.Symbol");

        // Assert - file should still be written (empty FileDescriptorSet)
        File.Exists(outputPath).ShouldBeTrue();

        var fileBytes = await File.ReadAllBytesAsync(outputPath, TestContext.Current.CancellationToken);
        var parsedFds = FileDescriptorSet.Parser.ParseFrom(fileBytes);

        parsedFds.ShouldNotBeNull();
        parsedFds.File.ShouldBeEmpty();
    }

    #endregion

    #region WriteProtosetAsync - Output Content Validation Tests

    [Fact]
    public async Task WriteProtosetAsync_NoSymbols_OutputContainsAllSourceFiles()
    {
        // Arrange
        var source = await ProtosetSource.LoadFromFileAsync(_testProtosetPath, TestContext.Current.CancellationToken);
        var outputPath = CreateTempFilePath();

        // Act
        await ProtosetExporter.WriteProtosetAsync(source, outputPath);

        // Assert - with no symbols, it should include all files from the source
        var fileBytes = await File.ReadAllBytesAsync(outputPath, TestContext.Current.CancellationToken);
        var parsedFds = FileDescriptorSet.Parser.ParseFrom(fileBytes);
        var sourceFileCount = source.FileDescriptorSet!.File.Count;

        parsedFds.File.Count.ShouldBe(sourceFileCount);
    }

    [Fact]
    public async Task WriteProtosetAsync_WithWellKnownTypes_WritesValidFile()
    {
        // Arrange
        var paths = new[] { _testProtosetPath, _wellKnownTypesProtosetPath };
        var source = await ProtosetSource.LoadFromFilesAsync(paths, TestContext.Current.CancellationToken);
        var outputPath = CreateTempFilePath();

        // Act
        await ProtosetExporter.WriteProtosetAsync(source, outputPath);

        // Assert
        File.Exists(outputPath).ShouldBeTrue();

        var fileBytes = await File.ReadAllBytesAsync(outputPath, TestContext.Current.CancellationToken);
        var parsedFds = FileDescriptorSet.Parser.ParseFrom(fileBytes);

        parsedFds.ShouldNotBeNull();
        parsedFds.File.ShouldNotBeEmpty();
    }

    #endregion

    #region WriteProtosetAsync - Overwrite Tests

    [Fact]
    public async Task WriteProtosetAsync_ExistingFile_WithoutForce_Throws()
    {
        // Arrange
        var source = await ProtosetSource.LoadFromFileAsync(_testProtosetPath, TestContext.Current.CancellationToken);
        var outputPath = CreateTempFilePath();

        // Act
        await File.WriteAllTextAsync(outputPath, "initial content", TestContext.Current.CancellationToken);

        // Assert
        var ex = await Should.ThrowAsync<IOException>(() =>
            ProtosetExporter.WriteProtosetAsync(source, outputPath));

        ex.Message.ShouldContain("Refusing to overwrite");
        ex.Message.ShouldContain(outputPath);

        // File should be unchanged.
        var contents = await File.ReadAllTextAsync(outputPath, TestContext.Current.CancellationToken);

        contents.ShouldBe("initial content");
    }

    [Fact]
    public async Task WriteProtosetAsync_ExistingFile_WithForce_Overwrites()
    {
        // Arrange
        var source = await ProtosetSource.LoadFromFileAsync(_testProtosetPath, TestContext.Current.CancellationToken);
        var outputPath = CreateTempFilePath();

        await File.WriteAllTextAsync(outputPath, "initial content", TestContext.Current.CancellationToken);

        await ProtosetExporter.WriteProtosetAsync(source, outputPath, force: true, []);

        var fileBytes = await File.ReadAllBytesAsync(outputPath, TestContext.Current.CancellationToken);

        // Act
        var parsedFds = FileDescriptorSet.Parser.ParseFrom(fileBytes);

        // Assert
        parsedFds.ShouldNotBeNull();
        parsedFds.File.ShouldNotBeEmpty();
    }

    #endregion

    public void Dispose()
    {
        foreach (var tempFile in _tempFiles)
        {
            try
            {
                if (File.Exists(tempFile))
                {
                    File.Delete(tempFile);
                }
            }
            catch
            {
                // Best-effort cleanup
            }
        }
    }

    private string CreateTempFilePath()
    {
        var path = Path.Combine(Path.GetTempPath(), $"protoset_export_test_{Guid.NewGuid()}.protoset");

        _tempFiles.Add(path);

        return path;
    }
}
