using GrpCurl.Net.Commands;

namespace GrpCurl.Net.Tests.Unit.Commands;

public sealed class GrpcurlCompatHandlerTests
{
    [Fact]
    public void TryRewrite_NoUpstreamFlags_ReturnsNull()
    {
        // Arrange

        // Assert

        // Act
        GrpcurlCompatHandler.TryRewrite(["list", "--plaintext", "localhost:9090"]).ShouldBeNull();
    }

    [Fact]
    public void TryRewrite_HelpFlag_ReturnsNull()
    {
        // Arrange

        // Assert

        // Act
        GrpcurlCompatHandler.TryRewrite(["--help"]).ShouldBeNull();
    }

    [Fact]
    public void TryRewrite_NativeSubcommand_LeavesArgsAlone()
    {
        // Arrange

        // Assert

        // Act
        GrpcurlCompatHandler.TryRewrite(["invoke", "-plaintext", "localhost:9090", "svc/M"]).ShouldBeNull();
    }

    [Fact]
    public void TryRewrite_PlaintextListOnly_RewritesToListSubcommand()
    {
        // Arrange

        // Act
        var rewritten = GrpcurlCompatHandler.TryRewrite(["-plaintext", "localhost:9090"]);

        // Assert
        rewritten.ShouldNotBeNull();
        rewritten[0].ShouldBe("list");
        rewritten.ShouldContain("--plaintext");
        rewritten.ShouldContain("localhost:9090");
    }

    [Fact]
    public void TryRewrite_DescribeSymbol_RewritesToDescribe()
    {
        // Arrange
        var rewritten = GrpcurlCompatHandler.TryRewrite([
            "-plaintext",
            "localhost:9090",
            "my.pkg.Service"

        // Act
        ]);

        // Assert
        rewritten.ShouldNotBeNull();
        rewritten[0].ShouldBe("describe");
        rewritten.ShouldContain("my.pkg.Service");
    }

    [Fact]
    public void TryRewrite_MethodWithSlash_RewritesToInvoke()
    {
        // Arrange
        var rewritten = GrpcurlCompatHandler.TryRewrite([
            "-plaintext",
            "-d",
            "{\"x\":1}",
            "localhost:9090",
            "my.pkg.Service/MyMethod"

        // Act
        ]);

        // Assert
        rewritten.ShouldNotBeNull();
        rewritten![0].ShouldBe("invoke");
        rewritten.ShouldContain("--plaintext");
        rewritten.ShouldContain("--data");
        rewritten.ShouldContain("{\"x\":1}");
        rewritten.ShouldContain("my.pkg.Service/MyMethod");
    }

    [Fact]
    public void TryRewrite_EqualsForm_StillRouted()
    {
        // Arrange
        var rewritten = GrpcurlCompatHandler.TryRewrite([
            "-plaintext",
            "-max-time=30s",
            "localhost:9090",
            "my.pkg.Service/MyMethod"

        // Act
        ]);

        // Assert
        rewritten.ShouldNotBeNull();
        rewritten.ShouldContain("--max-time");
        rewritten.ShouldContain("30s");
    }

    [Fact]
    public void TryRewrite_ImportPathAlias_NormalisedToDoubleDash()
    {
        // Arrange
        var rewritten = GrpcurlCompatHandler.TryRewrite([
            "-plaintext",
            "-I", "./protos",
            "-proto", "svc.proto",
            "localhost:9090",
            "pkg.Svc/M"

        // Act
        ]);

        // Assert
        rewritten.ShouldNotBeNull();
        rewritten.ShouldContain("--import-path");
        rewritten.ShouldContain("./protos");
        rewritten.ShouldContain("--proto");
        rewritten.ShouldContain("svc.proto");
    }
}
