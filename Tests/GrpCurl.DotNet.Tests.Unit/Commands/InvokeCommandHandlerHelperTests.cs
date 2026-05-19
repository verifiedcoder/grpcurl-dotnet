using Google.Protobuf.Reflection;
using Grpc.Core;
using GrpCurl.Net.Commands;
using GrpCurl.Net.DescriptorSources;
using GrpCurl.Net.Tests.Unit.Fixtures;

namespace GrpCurl.Net.Tests.Unit.Commands;

[Collection(ConsoleStreamCollection.Name)]
public sealed class InvokeCommandHandlerHelperTests
{
    #region WriteVerboseMethodInfo Tests

    [Fact]
    public async Task WriteVerboseMethodInfo_UnaryMethod_OutputsCorrectFormat()
    {
        // Arrange
        var protosetPath = Path.Combine(
            Path.GetDirectoryName(typeof(TestDescriptorProvider).Assembly.Location)!,
            "TestProtosets",
            "test.protoset");

        var source = await ProtosetSource.LoadFromFilesAsync([protosetPath], TestContext.Current.CancellationToken);
        var descriptor = await source.FindSymbolAsync("testing.TestService", TestContext.Current.CancellationToken);
        var svcDescriptor = (ServiceDescriptor)descriptor!;
        var unaryMethod = svcDescriptor.Methods.First(m => m.Name == "UnaryCall");
        var metadata = new Metadata();

        var originalError = Console.Error;

        await using var writer = new StringWriter();

        Console.SetError(writer);

        try
        {
            // Act
            InvokeCommandHandler.WriteVerboseMethodInfo(unaryMethod, metadata);

            // Assert
            var output = writer.ToString();

            output.ShouldContain("Resolved method descriptor:");
            output.ShouldContain("rpc UnaryCall");
            output.ShouldContain(".testing.SimpleRequest");
            output.ShouldContain(".testing.SimpleResponse");
        }
        finally
        {
            Console.SetError(originalError);
        }
    }

    [Fact]
    public async Task WriteVerboseMethodInfo_StreamingMethod_IncludesStreamPrefix()
    {
        // Arrange
        var protosetPath = Path.Combine(
            Path.GetDirectoryName(typeof(TestDescriptorProvider).Assembly.Location)!,
            "TestProtosets",
            "test.protoset");

        var source = await ProtosetSource.LoadFromFilesAsync([protosetPath], TestContext.Current.CancellationToken);
        var descriptor = await source.FindSymbolAsync("testing.TestService", TestContext.Current.CancellationToken);
        var svcDescriptor = (ServiceDescriptor)descriptor!;
        var streamingMethod = svcDescriptor.Methods.First(m => m.Name == "StreamingOutputCall");
        var metadata = new Metadata();

        var originalError = Console.Error;

        await using var writer = new StringWriter();

        Console.SetError(writer);

        try
        {
            // Act
            InvokeCommandHandler.WriteVerboseMethodInfo(streamingMethod, metadata);

            // Assert
            var output = writer.ToString();

            output.ShouldContain("stream");
            output.ShouldContain("rpc StreamingOutputCall");
        }
        finally
        {
            Console.SetError(originalError);
        }
    }

    [Fact]
    public async Task WriteVerboseMethodInfo_WithMetadata_RedactsSensitiveByDefault()
    {
        // Arrange
        var protosetPath = Path.Combine(
            Path.GetDirectoryName(typeof(TestDescriptorProvider).Assembly.Location)!,
            "TestProtosets",
            "test.protoset");

        var source = await ProtosetSource.LoadFromFilesAsync([protosetPath], TestContext.Current.CancellationToken);
        var descriptor = await source.FindSymbolAsync("testing.TestService", TestContext.Current.CancellationToken);
        var svcDescriptor = (ServiceDescriptor)descriptor!;
        var unaryMethod = svcDescriptor.Methods.First(m => m.Name == "UnaryCall");
        var metadata = new Metadata
        {
            { "authorization", "Bearer token123" },
            { "x-custom-header", "custom-value" }
        };

        var originalError = Console.Error;

        await using var writer = new StringWriter();

        Console.SetError(writer);

        try
        {
            // Act: default mode redacts authorization, keeps non-sensitive header verbatim.
            InvokeCommandHandler.WriteVerboseMethodInfo(unaryMethod, metadata);

            // Assert
            var output = writer.ToString();

            output.ShouldContain("Request metadata to send:");
            output.ShouldContain("authorization: [REDACTED]");
            output.ShouldNotContain("Bearer token123");
            output.ShouldContain("x-custom-header: custom-value");
        }
        finally
        {
            Console.SetError(originalError);
        }
    }

    [Fact]
    public async Task WriteVerboseMethodInfo_WithUnsafeShowSecrets_RevealsAllValues()
    {
        // Arrange
        var protosetPath = Path.Combine(
            Path.GetDirectoryName(typeof(TestDescriptorProvider).Assembly.Location)!,
            "TestProtosets",
            "test.protoset");

        var source = await ProtosetSource.LoadFromFilesAsync([protosetPath], TestContext.Current.CancellationToken);
        var descriptor = await source.FindSymbolAsync("testing.TestService", TestContext.Current.CancellationToken);
        var svcDescriptor = (ServiceDescriptor)descriptor!;
        var unaryMethod = svcDescriptor.Methods.First(m => m.Name == "UnaryCall");
        var metadata = new Metadata
        {
            { "authorization", "Bearer token123" },
            { "cookie", "session=abc" }
        };

        var originalError = Console.Error;

        await using var writer = new StringWriter();

        Console.SetError(writer);

        // Act
        try

        // Assert
        {
            InvokeCommandHandler.WriteVerboseMethodInfo(unaryMethod, metadata, unsafeShowSecrets: true);

            var output = writer.ToString();

            output.ShouldContain("authorization: Bearer token123");
            output.ShouldContain("cookie: session=abc");
            output.ShouldNotContain("[REDACTED]");
        }
        finally
        {
            Console.SetError(originalError);
        }
    }

    [Fact]
    public async Task WriteVerboseMethodInfo_EmptyMetadata_ShowsEmpty()
    {
        // Arrange
        var protosetPath = Path.Combine(
            Path.GetDirectoryName(typeof(TestDescriptorProvider).Assembly.Location)!,
            "TestProtosets",
            "test.protoset");

        var source = await ProtosetSource.LoadFromFilesAsync([protosetPath], TestContext.Current.CancellationToken);
        var descriptor = await source.FindSymbolAsync("testing.TestService", TestContext.Current.CancellationToken);
        var svcDescriptor = (ServiceDescriptor)descriptor!;
        var unaryMethod = svcDescriptor.Methods.First(m => m.Name == "UnaryCall");
        var metadata = new Metadata();

        var originalError = Console.Error;

        await using var writer = new StringWriter();

        Console.SetError(writer);

        try
        {
            // Act
            InvokeCommandHandler.WriteVerboseMethodInfo(unaryMethod, metadata);

            // Assert
            var output = writer.ToString();

            output.ShouldContain("Request metadata to send:");
            output.ShouldContain("(empty)");
        }
        finally
        {
            Console.SetError(originalError);
        }
    }

    #endregion

    #region WriteVerboseResponseHeaders Tests

    [Fact]
    public void WriteVerboseResponseHeaders_Null_ShowsEmpty()
    {
        // Arrange
        var originalError = Console.Error;

        using var writer = new StringWriter();

        Console.SetError(writer);

        try
        {
            // Act
            InvokeCommandHandler.WriteVerboseResponseHeaders(null);

            // Assert
            var output = writer.ToString();

            output.ShouldContain("Response headers received:");
            output.ShouldContain("(empty)");
        }
        finally
        {
            Console.SetError(originalError);
        }
    }

    [Fact]
    public void WriteVerboseResponseHeaders_Empty_ShowsEmpty()
    {
        // Arrange
        var headers = new Metadata();

        var originalError = Console.Error;

        using var writer = new StringWriter();

        Console.SetError(writer);

        try
        {
            // Act
            InvokeCommandHandler.WriteVerboseResponseHeaders(headers);

            // Assert
            var output = writer.ToString();

            output.ShouldContain("Response headers received:");
            output.ShouldContain("(empty)");
        }
        finally
        {
            Console.SetError(originalError);
        }
    }

    [Fact]
    public void WriteVerboseResponseHeaders_WithEntries_ListsThem()
    {
        // Arrange
        var headers = new Metadata
        {
            { "content-type", "application/grpc" },
            { "x-request-id", "abc-123" }
        };

        var originalError = Console.Error;

        using var writer = new StringWriter();

        Console.SetError(writer);

        try
        {
            // Act
            InvokeCommandHandler.WriteVerboseResponseHeaders(headers);

            // Assert
            var output = writer.ToString();

            output.ShouldContain("Response headers received:");
            output.ShouldContain("content-type: application/grpc");
            output.ShouldContain("x-request-id: abc-123");
        }
        finally
        {
            Console.SetError(originalError);
        }
    }

    #endregion

    #region WriteVerboseResponseTrailers Tests

    [Fact]
    public void WriteVerboseResponseTrailers_Null_ShowsEmpty()
    {
        // Arrange
        var originalError = Console.Error;


        using var writer = new StringWriter();

        Console.SetError(writer);

        try
        {
            // Act
            InvokeCommandHandler.WriteVerboseResponseTrailers(null);

            // Assert
            var output = writer.ToString();

            output.ShouldContain("Response trailers received:");
            output.ShouldContain("(empty)");
        }
        finally
        {
            Console.SetError(originalError);
        }
    }

    [Fact]
    public void WriteVerboseResponseTrailers_WithEntries_ListsThem()
    {
        // Arrange
        var trailers = new Metadata
        {
            { "grpc-status", "0" },
            { "grpc-message", "OK" }
        };

        var originalError = Console.Error;

        using var writer = new StringWriter();

        Console.SetError(writer);

        try
        {
            // Act
            InvokeCommandHandler.WriteVerboseResponseTrailers(trailers);

            // Assert
            var output = writer.ToString();
            output.ShouldContain("Response trailers received:");
            output.ShouldContain("grpc-status: 0");
            output.ShouldContain("grpc-message: OK");
        }
        finally
        {
            Console.SetError(originalError);
        }
    }

    #endregion
}
