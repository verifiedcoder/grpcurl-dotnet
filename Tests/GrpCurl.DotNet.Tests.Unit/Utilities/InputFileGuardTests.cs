using GrpCurl.Net.Utilities;

namespace GrpCurl.Net.Tests.Unit.Utilities;

public sealed class InputFileGuardTests
{
    [Fact]
    public async Task ReadAllTextAsync_FileWithinLimit_ReturnsContents()
    {
        // Arrange
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".txt");

        await File.WriteAllTextAsync(path, "hello", TestContext.Current.CancellationToken);

        try
        {
            // Act
            var result = await InputFileGuard.ReadAllTextAsync(
                path,
                maxBytes: 5,
                "test file",
                TestContext.Current.CancellationToken);

            // Assert
            result.ShouldBe("hello");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task ReadAllTextAsync_FileExceedsLimit_ThrowsInvalidDataException()
    {
        // Arrange
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".txt");

        await File.WriteAllTextAsync(path, "hello", TestContext.Current.CancellationToken);

        try
        {
            // Act
            var exception = await Should.ThrowAsync<InvalidDataException>(() =>
                InputFileGuard.ReadAllTextAsync(
                    path,
                    maxBytes: 4,
                    "test file",
                    TestContext.Current.CancellationToken));

            // Assert
            exception.Message.ShouldContain("test file");
            exception.Message.ShouldContain("maximum allowed");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task ReadAllBytesAsync_FileWithinLimit_ReturnsBytes()
    {
        // Arrange
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".bin");

        await File.WriteAllBytesAsync(path, [1, 2, 3], TestContext.Current.CancellationToken);

        try
        {
            // Act
            var result = await InputFileGuard.ReadAllBytesAsync(
                path,
                maxBytes: 3,
                "binary file",
                TestContext.Current.CancellationToken);

            // Assert
            result.ShouldBe([1, 2, 3]);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
