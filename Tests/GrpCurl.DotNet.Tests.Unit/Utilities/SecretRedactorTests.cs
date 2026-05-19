using Grpc.Core;
using GrpCurl.Net.Utilities;

namespace GrpCurl.Net.Tests.Unit.Utilities;

public sealed class SecretRedactorTests
{
    [Theory]
    [InlineData("authorization")]
    [InlineData("Authorization")]
    [InlineData("AUTHORIZATION")]
    [InlineData("proxy-authorization")]
    [InlineData("cookie")]
    [InlineData("set-cookie")]
    [InlineData("x-api-key")]
    [InlineData("X-API-KEY")]
    [InlineData("x-auth-token")]
    [InlineData("x-access-token")]
    [InlineData("x-csrf-token")]
    [InlineData("x-amz-security-token")]
    [InlineData("x-tenant-secret")]
    [InlineData("custom-api-key")]
    [InlineData("X-Service-Token")]
    [InlineData("api_key")]
    [InlineData("session-credential")]
    [InlineData("request-signature")]
    [InlineData("oauth-nonce")]
    [InlineData("user-jwt")]
    [InlineData("user-password")]
    [InlineData("trace-context-bin")]
    [InlineData("grpc-status-details-bin")]
    [InlineData("custom-metadata-bin")]
    public void ShouldRedact_SensitiveHeaders_ReturnsTrue(string headerName)
    {
        // Arrange

        // Act
        var result = SecretRedactor.ShouldRedact(headerName);

        // Assert
        result.ShouldBeTrue();
    }

    [Theory]
    [InlineData("user-agent")]
    [InlineData("content-type")]
    [InlineData("accept")]
    [InlineData("traceparent")]
    [InlineData("x-request-id")]
    [InlineData("grpc-encoding")]
    [InlineData("grpc-accept-encoding")]
    public void ShouldRedact_NonSensitiveHeaders_ReturnsFalse(string headerName)
    {
        // Arrange

        // Act
        var result = SecretRedactor.ShouldRedact(headerName);

        // Assert
        result.ShouldBeFalse();
    }

    [Fact]
    public void ShouldRedact_NullOrEmpty_ReturnsFalse()
    {
        // Arrange

        // Act
        var nullResult = SecretRedactor.ShouldRedact(null!);
        var emptyResult = SecretRedactor.ShouldRedact(string.Empty);

        // Assert
        nullResult.ShouldBeFalse();
        emptyResult.ShouldBeFalse();
    }

    [Fact]
    public void FormatValue_SensitiveHeader_ReturnsRedactedPlaceholder()
    {
        // Arrange

        // Act
        var result = SecretRedactor.FormatValue("authorization", "Bearer eyJ.example.token", unsafeShowSecrets: false);

        // Assert
        result.ShouldBe("[REDACTED]");
    }

    [Fact]
    public void FormatValue_SensitiveHeader_UnsafeShowSecrets_ReturnsRawValue()
    {
        // Arrange

        // Act
        var result = SecretRedactor.FormatValue("authorization", "Bearer eyJ.example.token", unsafeShowSecrets: true);

        // Assert
        result.ShouldBe("Bearer eyJ.example.token");
    }

    [Fact]
    public void FormatValue_NonSensitiveHeader_ReturnsRawValue()
    {
        // Arrange

        // Act
        var result = SecretRedactor.FormatValue("user-agent", "grpcurl.net/1.0", unsafeShowSecrets: false);

        // Assert
        result.ShouldBe("grpcurl.net/1.0");
    }

    [Fact]
    public void FormatValue_NonSensitiveHeaderWithControlCharacters_ReturnsEscapedValue()
    {
        // Arrange
        const string value = "line1\r\nline2\t\u001B";

        // Act
        var result = SecretRedactor.FormatValue("x-debug", value, unsafeShowSecrets: false);

        // Assert
        result.ShouldBe("line1\\r\\nline2\\t\\u001B");
    }

    [Fact]
    public void FormatValue_SensitiveHeaderWithUnsafeShowSecrets_ReturnsEscapedValue()
    {
        // Arrange
        const string value = "secret\nsecond-line";

        // Act
        var result = SecretRedactor.FormatValue("authorization", value, unsafeShowSecrets: true);

        // Assert
        result.ShouldBe("secret\\nsecond-line");
    }

    [Fact]
    public void FormatLines_MixedMetadata_RedactsOnlySensitive()
    {
        // Arrange
        var metadata = new Metadata
        {
            { "user-agent", "grpcurl.net/1.0" },
            { "authorization", "Bearer secret-token" },
            { "x-request-id", "abc123" },
            { "cookie", "session=xyz" }
        };

        // Act
        var lines = SecretRedactor.FormatLines(metadata, unsafeShowSecrets: false).ToArray();

        // Assert
        lines.ShouldContain("user-agent: grpcurl.net/1.0");
        lines.ShouldContain("authorization: [REDACTED]");
        lines.ShouldContain("x-request-id: abc123");
        lines.ShouldContain("cookie: [REDACTED]");
    }

    [Fact]
    public void FormatLines_UnsafeShowSecrets_RevealsAllValues()
    {
        // Arrange
        var metadata = new Metadata
        {
            { "authorization", "Bearer secret-token" },
            { "cookie", "session=xyz" }
        };

        // Act
        var lines = SecretRedactor.FormatLines(metadata, unsafeShowSecrets: true).ToArray();

        // Assert
        lines.ShouldContain("authorization: Bearer secret-token");
        lines.ShouldContain("cookie: session=xyz");
    }

    [Fact]
    public void FormatLines_BinaryMetadata_RedactedByDefault_RevealedWhenUnsafe()
    {
        // Arrange
        var metadata = new Metadata
        {
            { "trace-bin", [1, 2, 3, 4] }
        };

        var redacted = SecretRedactor.FormatLines(metadata, unsafeShowSecrets: false).Single();

        // Act
        var revealed = SecretRedactor.FormatLines(metadata, unsafeShowSecrets: true).Single();

        // Assert
        redacted.ShouldBe("trace-bin: [REDACTED]");
        revealed.ShouldStartWith("trace-bin: ");
        revealed.ShouldEndWith("AQIDBA==");
    }
}
