using Grpc.Core;
using GrpCurl.Net.Invocation;
using GrpCurl.Net.Tests.Unit.Fixtures;

namespace GrpCurl.Net.Tests.Unit.Invocation;

public sealed class InvocationResultTests
{
    [Fact]
    public void InvocationResult_HasResponse_ReturnsMessage()
    {
        // Arrange
        var descriptor = TestDescriptorProvider.SimpleRequest;
        var message = new SimpleDynamicMessage(descriptor);

        // Act
        var result = new InvocationResult
        {
            Response = message
        };

        // Assert
        _ = result.Response.ShouldNotBeNull();
        result.Response.ShouldBeSameAs(message);
    }

    [Fact]
    public void InvocationResult_HasHeaders_ReturnsMetadata()
    {
        // Arrange
        var descriptor = TestDescriptorProvider.SimpleRequest;
        var message = new SimpleDynamicMessage(descriptor);
        var headers = new Metadata
        {
            { "content-type", "application/grpc" }
        };

        // Act
        var result = new InvocationResult
        {
            Response = message,
            ResponseHeaders = headers
        };

        // Assert
        _ = result.ResponseHeaders.ShouldNotBeNull();
        result.ResponseHeaders.Count.ShouldBe(1);
    }

    [Fact]
    public void InvocationResult_HasTrailers_ReturnsMetadata()
    {
        // Arrange
        var descriptor = TestDescriptorProvider.SimpleRequest;
        var message = new SimpleDynamicMessage(descriptor);
        var trailers = new Metadata
        {
            { "grpc-status", "0" }
        };

        // Act
        var result = new InvocationResult
        {
            Response = message,
            ResponseTrailers = trailers
        };

        // Assert
        _ = result.ResponseTrailers.ShouldNotBeNull();
        result.ResponseTrailers.Count.ShouldBe(1);
    }

    [Fact]
    public void InvocationResult_NullHeaders_ReturnsNull()
    {
        // Arrange
        var descriptor = TestDescriptorProvider.SimpleRequest;
        var message = new SimpleDynamicMessage(descriptor);

        // Act
        var result = new InvocationResult
        {
            Response = message,
            ResponseHeaders = null,
            ResponseTrailers = null
        };

        // Assert
        result.ResponseHeaders.ShouldBeNull();
        result.ResponseTrailers.ShouldBeNull();
    }
}
