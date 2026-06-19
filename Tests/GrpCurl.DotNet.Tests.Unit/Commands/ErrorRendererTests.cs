using GrpCurl.Net.Commands;
using GrpCurl.Net.Exceptions;
using System.Text.Json;

namespace GrpCurl.Net.Tests.Unit.Commands;

[Collection("EnvironmentVariables")]
public sealed class ErrorRendererTests
{
    [Fact]
    public void Render_RpcCategory_Json_EmitsExpectedShape()
    {
        // Arrange
        var envelope = new ErrorEnvelope
        {
            Category = ErrorCategory.Rpc,
            ExitCode = 78,
            Message = "key missing",
            Address = "localhost:9090",
            Method = "testing.TestService/Get",
            Hint = "check the symbol name spelling",
            Grpc = new RpcErrorInfo
            {
                Code = 5,
                Status = "NotFound",
                Detail = "key missing"
            }
        };

        var (stderr, _) = CaptureStreams(w => ErrorRenderer.Render(envelope, OutputFormat.Json, w));

        // Act
        var line = stderr.Trim();

        // Assert
        line.ShouldStartWith("{");
        line.ShouldEndWith("}");

        using var doc = JsonDocument.Parse(line);
        var root = doc.RootElement;

        root.GetProperty("kind").GetString().ShouldBe("error");
        root.GetProperty("category").GetString().ShouldBe("rpc");
        root.GetProperty("exitCode").GetInt32().ShouldBe(78);
        root.GetProperty("message").GetString().ShouldBe("key missing");
        root.GetProperty("address").GetString().ShouldBe("localhost:9090");
        root.GetProperty("method").GetString().ShouldBe("testing.TestService/Get");
        root.GetProperty("hint").GetString().ShouldBe("check the symbol name spelling");

        var grpc = root.GetProperty("grpc");

        grpc.GetProperty("code").GetInt32().ShouldBe(5);
        grpc.GetProperty("status").GetString().ShouldBe("NotFound");
        grpc.GetProperty("detail").GetString().ShouldBe("key missing");
    }

    [Fact]
    public void Render_UsageCategory_Json_OmitsNullFields()
    {
        // Arrange
        var envelope = new ErrorEnvelope
        {
            Category = ErrorCategory.Usage,
            ExitCode = 2,
            Message = "Method must be in format 'Service/Method'"
        };

        var (stderr, _) = CaptureStreams(w => ErrorRenderer.Render(envelope, OutputFormat.Json, w));

        var line = stderr.Trim();

        using var doc = JsonDocument.Parse(line);

        // Act
        var root = doc.RootElement;

        // Assert
        root.GetProperty("category").GetString().ShouldBe("usage");
        root.GetProperty("exitCode").GetInt32().ShouldBe(2);
        root.TryGetProperty("address", out _).ShouldBeFalse();
        root.TryGetProperty("method", out _).ShouldBeFalse();
        root.TryGetProperty("grpc", out _).ShouldBeFalse();
        root.TryGetProperty("hint", out _).ShouldBeFalse();
    }

    [Theory]
    [InlineData("Schema", "schema")]
    [InlineData("Network", "network")]
    [InlineData("Timeout", "timeout")]
    [InlineData("Cancelled", "cancelled")]
    [InlineData("Internal", "internal")]
    public void Render_AllCategories_SerializeAsLowerCase(string categoryName, string expected)
    {
        // Arrange
        var category = Enum.Parse<ErrorCategory>(categoryName);
        var envelope = new ErrorEnvelope
        {
            Category = category,
            ExitCode = 1,
            Message = "test"
        };

        var (stderr, _) = CaptureStreams(w => ErrorRenderer.Render(envelope, OutputFormat.Json, w));

        // Act
        using var doc = JsonDocument.Parse(stderr.Trim());

        // Assert
        doc.RootElement.GetProperty("category").GetString().ShouldBe(expected);
    }

    [Fact]
    public void Render_SpecialCharactersInMessage_AreProperlyEscapedInJson()
    {
        // Arrange
        var envelope = new ErrorEnvelope
        {
            Category = ErrorCategory.Rpc,
            ExitCode = 64,
            Message = "error with \"quotes\" and \\backslash and \n newline",
            Grpc = new RpcErrorInfo
            {
                Code = 13,
                Status = "Internal",
                Detail = "error with \"quotes\" and \\backslash and \n newline"
            }
        };

        var (stderr, _) = CaptureStreams(w => ErrorRenderer.Render(envelope, OutputFormat.Json, w));

        var line = stderr.Trim();

        // Must round-trip through System.Text.Json without errors.
        using var doc = JsonDocument.Parse(line);

        // Act
        var message = doc.RootElement.GetProperty("message").GetString()!;

        // Assert
        message.ShouldContain("\"quotes\"");
        message.ShouldContain("\\backslash");
        message.ShouldContain("\n");
    }

    [Fact]
    public void Render_TextMode_GoesToStderr_NotStdout()
    {
        // Arrange
        var envelope = new ErrorEnvelope
        {
            Category = ErrorCategory.Network,
            ExitCode = 4,
            Message = "Failed to connect to localhost:65535"
        };

        // Act
        var (stderr, stdout) = CaptureStreams(w => ErrorRenderer.Render(envelope, OutputFormat.Text, w));

        // Assert
        stdout.ShouldBeEmpty();
        stderr.ShouldContain("Connection Error");
        stderr.ShouldContain("Failed to connect to localhost:65535");
    }

    [Fact]
    public void Render_JsonMode_GoesToStderr_NotStdout()
    {
        // Arrange
        var envelope = new ErrorEnvelope
        {
            Category = ErrorCategory.Network,
            ExitCode = 4,
            Message = "Failed to connect to localhost:65535"
        };

        // Act
        var (stderr, stdout) = CaptureStreams(w => ErrorRenderer.Render(envelope, OutputFormat.Json, w));

        // Assert
        stdout.ShouldBeEmpty();
        stderr.ShouldStartWith("{");
    }

    [Fact]
    public void AppendProxyHintIfRelevant_NetworkErrorWithProxySet_AddsSuggestion()
    {
        WithProxyEnvironment(("HTTP_PROXY", "http://proxy.corp:3128"), () =>
        {
            var envelope = new ErrorEnvelope
            {
                Category = ErrorCategory.Network,
                ExitCode = 4,
                Message = "Failed to connect to localhost:9090",
                Address = "localhost:9090"
            };

            var result = ErrorRenderer.AppendProxyHintIfRelevant(envelope);

            result.Suggestions.ShouldContain(s => s.Contains("HTTP_PROXY") && s.Contains("localhost"));
        });
    }

    [Fact]
    public void AppendProxyHintIfRelevant_UnavailableRpcWithProxySet_AddsSuggestion()
    {
        WithProxyEnvironment(("HTTPS_PROXY", "http://proxy.corp:3128"), () =>
        {
            var envelope = new ErrorEnvelope
            {
                Category = ErrorCategory.Rpc,
                ExitCode = 78,
                Message = "connection refused",
                Address = "api.example.com:443",
                Grpc = new RpcErrorInfo { Code = 14, Status = "Unavailable", Detail = "connection refused" }
            };

            var result = ErrorRenderer.AppendProxyHintIfRelevant(envelope);

            result.Suggestions.ShouldContain(s => s.Contains("HTTPS_PROXY"));
        });
    }

    [Fact]
    public void AppendProxyHintIfRelevant_NoProxySet_LeavesEnvelopeUnchanged()
    {
        WithProxyEnvironment([], () =>
        {
            var envelope = new ErrorEnvelope
            {
                Category = ErrorCategory.Network,
                ExitCode = 4,
                Message = "Failed to connect",
                Address = "localhost:9090"
            };

            var result = ErrorRenderer.AppendProxyHintIfRelevant(envelope);

            result.ShouldBeSameAs(envelope);
        });
    }

    [Fact]
    public void AppendProxyHintIfRelevant_NonConnectionError_LeavesEnvelopeUnchanged()
    {
        WithProxyEnvironment(("HTTP_PROXY", "http://proxy.corp:3128"), () =>
        {
            var envelope = new ErrorEnvelope
            {
                Category = ErrorCategory.Schema,
                ExitCode = 3,
                Message = "Protoset file not found",
                Address = "localhost:9090"
            };

            var result = ErrorRenderer.AppendProxyHintIfRelevant(envelope);

            result.ShouldBeSameAs(envelope);
        });
    }

    private static void WithProxyEnvironment((string Name, string? Value)[] overrides, Action body)
    {
        string[] names = ["HTTP_PROXY", "http_proxy", "HTTPS_PROXY", "https_proxy", "ALL_PROXY", "all_proxy", "NO_PROXY", "no_proxy"];
        var saved = names.ToDictionary(n => n, Environment.GetEnvironmentVariable);

        try
        {
            foreach (var name in names)
            {
                Environment.SetEnvironmentVariable(name, null);
            }

            foreach (var (name, value) in overrides)
            {
                Environment.SetEnvironmentVariable(name, value);
            }

            body();
        }
        finally
        {
            foreach (var (name, value) in saved)
            {
                Environment.SetEnvironmentVariable(name, value);
            }
        }
    }

    private static void WithProxyEnvironment((string Name, string? Value) singleOverride, Action body)
        => WithProxyEnvironment([singleOverride], body);

    private static (string stderr, string stdout) CaptureStreams(Action<TextWriter> action)
    {
        using var stderrWriter = new StringWriter();

        action(stderrWriter);

        return (stderrWriter.ToString(), string.Empty);
    }
}
