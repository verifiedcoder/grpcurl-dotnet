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
    public void ShouldRedact_AlwaysSensitiveHeaders_ReturnsTrue(string headerName)
    {
        SecretRedactor.ShouldRedact(headerName).ShouldBeTrue();
    }

    [Theory]
    [InlineData("x-tenant-secret")]
    [InlineData("custom-api-key")]
    [InlineData("X-Service-Token")]
    [InlineData("api_key")]
    [InlineData("session-credential")]
    [InlineData("request-signature")]
    [InlineData("oauth-nonce")]
    [InlineData("user-jwt")]
    [InlineData("user-password")]
    public void ShouldRedact_SensitiveSuffix_ReturnsTrue(string headerName)
    {
        SecretRedactor.ShouldRedact(headerName).ShouldBeTrue();
    }

    [Theory]
    [InlineData("trace-context-bin")]
    [InlineData("grpc-status-details-bin")]
    [InlineData("custom-metadata-bin")]
    public void ShouldRedact_BinaryMetadata_ReturnsTrue(string headerName)
    {
        SecretRedactor.ShouldRedact(headerName).ShouldBeTrue();
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
        SecretRedactor.ShouldRedact(headerName).ShouldBeFalse();
    }

    [Fact]
    public void ShouldRedact_NullOrEmpty_ReturnsFalse()
    {
        SecretRedactor.ShouldRedact(null!).ShouldBeFalse();
        SecretRedactor.ShouldRedact(string.Empty).ShouldBeFalse();
    }

    [Fact]
    public void FormatValue_SensitiveHeader_ReturnsRedactedPlaceholder()
    {
        var result = SecretRedactor.FormatValue("authorization", "Bearer eyJ.example.token", unsafeShowSecrets: false);

        result.ShouldBe("[REDACTED]");
    }

    [Fact]
    public void FormatValue_SensitiveHeader_UnsafeShowSecrets_ReturnsRawValue()
    {
        var result = SecretRedactor.FormatValue("authorization", "Bearer eyJ.example.token", unsafeShowSecrets: true);

        result.ShouldBe("Bearer eyJ.example.token");
    }

    [Fact]
    public void FormatValue_NonSensitiveHeader_ReturnsRawValue()
    {
        var result = SecretRedactor.FormatValue("user-agent", "grpcurl.net/1.0", unsafeShowSecrets: false);

        result.ShouldBe("grpcurl.net/1.0");
    }

    [Fact]
    public void FormatLines_MixedMetadata_RedactsOnlySensitive()
    {
        var metadata = new Metadata
        {
            { "user-agent", "grpcurl.net/1.0" },
            { "authorization", "Bearer secret-token" },
            { "x-request-id", "abc123" },
            { "cookie", "session=xyz" }
        };

        var lines = SecretRedactor.FormatLines(metadata, unsafeShowSecrets: false).ToArray();

        lines.ShouldContain("user-agent: grpcurl.net/1.0");
        lines.ShouldContain("authorization: [REDACTED]");
        lines.ShouldContain("x-request-id: abc123");
        lines.ShouldContain("cookie: [REDACTED]");
    }

    [Fact]
    public void FormatLines_UnsafeShowSecrets_RevealsAllValues()
    {
        var metadata = new Metadata
        {
            { "authorization", "Bearer secret-token" },
            { "cookie", "session=xyz" }
        };

        var lines = SecretRedactor.FormatLines(metadata, unsafeShowSecrets: true).ToArray();

        lines.ShouldContain("authorization: Bearer secret-token");
        lines.ShouldContain("cookie: session=xyz");
    }

    [Fact]
    public void FormatLines_BinaryMetadata_RedactedByDefault_RevealedWhenUnsafe()
    {
        var metadata = new Metadata
        {
            { "trace-bin", new byte[] { 1, 2, 3, 4 } }
        };

        var redacted = SecretRedactor.FormatLines(metadata, unsafeShowSecrets: false).Single();
        var revealed = SecretRedactor.FormatLines(metadata, unsafeShowSecrets: true).Single();

        redacted.ShouldBe("trace-bin: [REDACTED]");
        revealed.ShouldStartWith("trace-bin: ");
        revealed.ShouldEndWith("AQIDBA==");
    }
}
