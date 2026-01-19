using GrpCurl.Net.Utilities;

namespace GrpCurl.Net.Tests.Unit.Utilities;

public sealed class GrpcChannelFactoryTests
{
    #region ParseDuration Tests

    [Theory]
    // Seconds
    [InlineData("10s", 10_000)]
    [InlineData("1s", 1_000)]
    [InlineData("0s", 0)]
    [InlineData("100s", 100_000)]
    // Milliseconds
    [InlineData("500ms", 500)]
    [InlineData("1ms", 1)]
    [InlineData("1000ms", 1000)]
    [InlineData("0ms", 0)]
    // Minutes
    [InlineData("1m", 60_000)]
    [InlineData("5m", 300_000)]
    [InlineData("0m", 0)]
    // Hours
    [InlineData("1h", 3_600_000)]
    [InlineData("2h", 7_200_000)]
    // Decimals
    [InlineData("1.5s", 1500)]
    [InlineData("0.5s", 500)]
    [InlineData("1.5m", 90_000)]
    [InlineData("0.5h", 1_800_000)]
    // Plain numbers (default to seconds)
    [InlineData("10", 10_000)]
    [InlineData("1", 1_000)]
    [InlineData("0", 0)]
    public void ParseDuration_ValidInputs_ParsesCorrectly(string input, int expectedMs)
    {
        // Arrange & Act
        var result = GrpcChannelFactory.ParseDuration(input);

        // Assert
        result.ShouldBe(TimeSpan.FromMilliseconds(expectedMs));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void ParseDuration_EmptyString_ThrowsException(string input)
    {
        // Arrange & Act & Assert
        var ex = Should.Throw<ArgumentException>(() => GrpcChannelFactory.ParseDuration(input));
        ex.Message.ShouldContain("empty");
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("10x")]
    [InlineData("s10")]
    [InlineData("10 seconds")]
    public void ParseDuration_InvalidFormat_ThrowsException(string input)
    {
        // Arrange & Act & Assert
        var ex = Should.Throw<ArgumentException>(() => GrpcChannelFactory.ParseDuration(input));
        ex.Message.ShouldContain("Invalid duration format");
    }

    #endregion

    #region ParseSize Tests

    [Theory]
    // Plain bytes (no unit)
    [InlineData("1024", 1024)]
    [InlineData("0", 0)]
    [InlineData("100", 100)]
    // Explicit bytes
    [InlineData("1B", 1)]
    [InlineData("100B", 100)]
    [InlineData("1024B", 1024)]
    // Kilobytes
    [InlineData("1KB", 1024)]
    [InlineData("10KB", 10240)]
    [InlineData("1kb", 1024)] // Case-insensitive
    // Megabytes
    [InlineData("1MB", 1048576)]
    [InlineData("4MB", 4194304)]
    [InlineData("10MB", 10485760)]
    [InlineData("1mb", 1048576)] // Case-insensitive
    // Gigabytes
    [InlineData("1GB", 1073741824)]
    [InlineData("1gb", 1073741824)] // Case-insensitive
    // Decimals
    [InlineData("1.5MB", 1572864)]
    [InlineData("0.5KB", 512)]
    public void ParseSize_ValidInputs_ParsesCorrectly(string input, int expectedBytes)
    {
        // Arrange & Act
        var result = GrpcChannelFactory.ParseSize(input);

        // Assert
        result.ShouldBe(expectedBytes);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void ParseSize_EmptyString_ThrowsException(string input)
    {
        // Arrange & Act & Assert
        var ex = Should.Throw<ArgumentException>(() => GrpcChannelFactory.ParseSize(input));
        ex.Message.ShouldContain("empty");
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("10TB")] // Unsupported unit
    [InlineData("MB10")]
    public void ParseSize_InvalidFormat_ThrowsException(string input)
    {
        // Arrange & Act & Assert
        var ex = Should.Throw<ArgumentException>(() => GrpcChannelFactory.ParseSize(input));
        ex.Message.ShouldContain("Invalid size format");
    }

    [Fact]
    public void ParseSize_Overflow_ThrowsException()
    {
        // Arrange & Act & Assert
        var ex = Should.Throw<ArgumentException>(() => GrpcChannelFactory.ParseSize("3GB"));
        ex.Message.ShouldContain("too large");
    }

    #endregion

    #region CreateMetadata Tests

    [Fact]
    public void CreateMetadata_NullHeaders_ReturnsDefaultUserAgent()
    {
        // Arrange & Act
        var metadata = GrpcChannelFactory.CreateMetadata(null);

        // Assert
        metadata.ShouldHaveSingleItem();
        metadata[0].Key.ShouldBe("user-agent");
        metadata[0].Value.ShouldBe("grpcurl-dotnet/1.0.0");
    }

    [Fact]
    public void CreateMetadata_EmptyHeaders_ReturnsDefaultUserAgent()
    {
        // Arrange & Act
        var metadata = GrpcChannelFactory.CreateMetadata([]);

        // Assert
        metadata.ShouldHaveSingleItem();
        metadata[0].Key.ShouldBe("user-agent");
    }

    [Fact]
    public void CreateMetadata_SingleHeader_ParsesCorrectly()
    {
        // Arrange & Act
        var metadata = GrpcChannelFactory.CreateMetadata(["Authorization: Bearer token123"]);

        // Assert
        metadata.Count.ShouldBe(2); // user-agent + Authorization
        metadata.ShouldContain(m => m.Key == "authorization" && m.Value == "Bearer token123");
    }

    [Fact]
    public void CreateMetadata_MultipleHeaders_ParsesCorrectly()
    {
        // Arrange
        var headers = new[]
        {
            "Authorization: Bearer token123",
            "X-Custom-Header: custom-value",
            "X-Another-Header: another-value"
        };

        // Act
        var metadata = GrpcChannelFactory.CreateMetadata(headers);

        // Assert
        metadata.Count.ShouldBe(4); // user-agent + 3 custom headers
        metadata.ShouldContain(m => m.Key == "authorization" && m.Value == "Bearer token123");
        metadata.ShouldContain(m => m.Key == "x-custom-header" && m.Value == "custom-value");
        metadata.ShouldContain(m => m.Key == "x-another-header" && m.Value == "another-value");
    }

    [Fact]
    public void CreateMetadata_CustomUserAgent_OverridesDefault()
    {
        // Arrange & Act
        var metadata = GrpcChannelFactory.CreateMetadata(null, "my-custom-agent/2.0");

        // Assert
        metadata.ShouldHaveSingleItem();
        metadata[0].Key.ShouldBe("user-agent");
        metadata[0].Value.ShouldBe("my-custom-agent/2.0");
    }

    [Fact]
    public void CreateMetadata_HeaderWithColonInValue_ParsesCorrectly()
    {
        // Arrange & Act
        var metadata = GrpcChannelFactory.CreateMetadata(["Time: 10:30:45"]);

        // Assert
        metadata.ShouldContain(m => m.Key == "time" && m.Value == "10:30:45");
    }

    [Fact]
    public void CreateMetadata_InvalidHeaderFormat_ThrowsException()
    {
        // Arrange & Act & Assert
        var ex = Should.Throw<ArgumentException>(() =>
            GrpcChannelFactory.CreateMetadata(["invalid-header-without-colon"]));
        ex.Message.ShouldContain("Invalid header format");
    }

    [Fact]
    public void CreateMetadata_WhitespaceHeader_IsSkipped()
    {
        // Arrange
        var headers = new[] { "   ", "" };

        // Act
        var metadata = GrpcChannelFactory.CreateMetadata(headers);

        // Assert
        metadata.ShouldHaveSingleItem(); // Should only have the user-agent
    }

    [Fact]
    public void CreateMetadata_HeaderTrimsWhitespace()
    {
        // Arrange & Act
        var metadata = GrpcChannelFactory.CreateMetadata(["  Key  :  Value  "]);

        // Assert
        metadata.ShouldContain(m => m.Key == "key" && m.Value == "Value");
    }

    #endregion

    #region Environment Variable Expansion Tests

    [Fact]
    public void CreateMetadata_ExpandsEnvironmentVariable()
    {
        // Arrange
        const string varName = "GRPCURL_TEST_VAR";
        const string varValue = "test-value-123";

        try
        {
            Environment.SetEnvironmentVariable(varName, varValue);

            // Act
            var metadata = GrpcChannelFactory.CreateMetadata([$"Authorization: Bearer ${{{varName}}}"]);

            // Assert
            metadata.ShouldContain(m => m.Key == "authorization" && m.Value == $"Bearer {varValue}");
        }
        finally
        {
            Environment.SetEnvironmentVariable(varName, null);
        }
    }

    [Fact]
    public void CreateMetadata_MultipleEnvironmentVariables()
    {
        // Arrange
        const string var1 = "GRPCURL_VAR1";
        const string var2 = "GRPCURL_VAR2";

        try
        {
            Environment.SetEnvironmentVariable(var1, "value1");
            Environment.SetEnvironmentVariable(var2, "value2");

            // Act
            var metadata = GrpcChannelFactory.CreateMetadata([$"Header: ${{{var1}}}-${{{var2}}}"]);

            // Assert
            metadata.ShouldContain(m => m.Key == "header" && m.Value == "value1-value2");
        }
        finally
        {
            Environment.SetEnvironmentVariable(var1, null);
            Environment.SetEnvironmentVariable(var2, null);
        }
    }

    [Fact]
    public void CreateMetadata_MissingEnvironmentVariable_ThrowsException()
    {
        // Arrange
        const string varName = "GRPCURL_NONEXISTENT_VAR_12345";
        Environment.SetEnvironmentVariable(varName, null);

        // Act & Assert
        var ex = Should.Throw<ArgumentException>(() =>
            GrpcChannelFactory.CreateMetadata([$"Header: ${{{varName}}}"]));
        ex.Message.ShouldContain(varName);
        ex.Message.ShouldContain("not found");
        ex.Message.ShouldContain("Header:"); // Verify header context is included
    }

    [Fact]
    public void CreateMetadata_EmptyEnvironmentVariable_Expands()
    {
        // Arrange
        const string varName = "GRPCURL_EMPTY_VAR";

        try
        {
            Environment.SetEnvironmentVariable(varName, "");

            // Act
            var metadata = GrpcChannelFactory.CreateMetadata([$"Header: prefix${{{varName}}}suffix"]);

            // Assert
            metadata.ShouldContain(m => m.Key == "header" && m.Value == "prefixsuffix");
        }
        finally
        {
            Environment.SetEnvironmentVariable(varName, null);
        }
    }

    #endregion

    #region Create Channel Tests

    [Fact]
    public void Create_WithHttpScheme_CreatesChannel()
    {
        // Arrange & Act
        using var channel = GrpcChannelFactory.Create("http://localhost:50051");

        // Assert
        channel.ShouldNotBeNull();
        channel.Target.ShouldContain("localhost");
    }

    [Fact]
    public void Create_WithHttpsScheme_CreatesChannel()
    {
        // Arrange & Act
        using var channel = GrpcChannelFactory.Create("https://localhost:50051");

        // Assert
        channel.ShouldNotBeNull();
        channel.Target.ShouldContain("localhost");
    }

    [Fact]
    public void Create_WithoutScheme_CreatesChannel()
    {
        // Arrange & Act
        using var channel = GrpcChannelFactory.Create("localhost:50051");

        // Assert
        channel.ShouldNotBeNull();
        channel.Target.ShouldContain("localhost");
    }

    [Fact]
    public void Create_WithPlaintextOption_CreatesChannel()
    {
        // Arrange & Act
        using var channel = GrpcChannelFactory.Create(
            "localhost:50051",
            new GrpcChannelFactory.ChannelOptions
            {
                Plaintext = true
            });

        // Assert
        channel.ShouldNotBeNull();
        channel.Target.ShouldContain("localhost");
    }

    [Fact]
    public void Create_WithNullOptions_UsesDefaults()
    {
        // Arrange & Act
        using var channel = GrpcChannelFactory.Create("https://localhost:50051");

        // Assert
        channel.ShouldNotBeNull();
    }

    #endregion

    #region ChannelOptions Tests

    [Fact]
    public void ChannelOptions_DefaultValues()
    {
        // Arrange & Act
        var options = new GrpcChannelFactory.ChannelOptions();

        // Assert
        options.Plaintext.ShouldBeFalse();
        options.InsecureSkipVerify.ShouldBeFalse();
        options.CaCertPath.ShouldBeNull();
        options.ClientCertPath.ShouldBeNull();
        options.ClientKeyPath.ShouldBeNull();
        options.ConnectTimeout.ShouldBeNull();
        options.KeepaliveTime.ShouldBeNull();
        options.MaxReceiveMessageSize.ShouldBeNull();
        options.MaxSendMessageSize.ShouldBeNull();
        options.Authority.ShouldBeNull();
        options.ServerName.ShouldBeNull();
    }

    [Fact]
    public void ChannelOptions_AllPropertiesSettable()
    {
        // Arrange & Act
        var options = new GrpcChannelFactory.ChannelOptions
        {
            Plaintext = true,
            InsecureSkipVerify = true,
            CaCertPath = "/path/to/ca.crt",
            ClientCertPath = "/path/to/client.crt",
            ClientKeyPath = "/path/to/client.key",
            ConnectTimeout = TimeSpan.FromSeconds(30),
            KeepaliveTime = TimeSpan.FromMinutes(1),
            MaxReceiveMessageSize = 4 * 1024 * 1024,
            MaxSendMessageSize = 4 * 1024 * 1024,
            Authority = "my-authority",
            ServerName = "my-server"
        };

        // Assert
        options.Plaintext.ShouldBeTrue();
        options.InsecureSkipVerify.ShouldBeTrue();
        options.CaCertPath.ShouldBe("/path/to/ca.crt");
        options.ClientCertPath.ShouldBe("/path/to/client.crt");
        options.ClientKeyPath.ShouldBe("/path/to/client.key");
        options.ConnectTimeout.ShouldBe(TimeSpan.FromSeconds(30));
        options.KeepaliveTime.ShouldBe(TimeSpan.FromMinutes(1));
        options.MaxReceiveMessageSize.ShouldBe(4 * 1024 * 1024);
        options.MaxSendMessageSize.ShouldBe(4 * 1024 * 1024);
        options.Authority.ShouldBe("my-authority");
        options.ServerName.ShouldBe("my-server");
    }

    #endregion
}
