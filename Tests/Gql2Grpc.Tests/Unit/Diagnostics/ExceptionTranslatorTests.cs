using System.Text.Json;
using Gql2Grpc.Diagnostics;
using Gql2Grpc.Translation;
using Grpc.Core;
using GrpCurl.Net.DescriptorSources;

namespace Gql2Grpc.Tests.Unit.Diagnostics;

public sealed class ExceptionTranslatorTests
{
    [Fact]
    public void UnknownArgument_maps_to_usage_error_with_code()
    {
        // Arrange
        var ex = new UnknownArgumentException("input", "input", "testing.SimpleRequest");

        // Act
        var error = ExceptionTranslator.ToFieldError(ex, "foo");
        var exitCode = ExceptionTranslator.ExitCodeFor(ex);

        // Assert
        error.Path.ShouldBe(["foo"]);
        error.Extensions.ShouldNotBeNull();
        error.Extensions!["code"].ShouldBe("UNKNOWN_ARGUMENT");
        error.Message.ShouldContain("testing.SimpleRequest");
        exitCode.ShouldBe(2);
    }

    [Fact]
    public void RpcException_translates_to_grpc_extensions_with_field_path()
    {
        // Arrange
        var rpc = new RpcException(new Status(StatusCode.Unavailable, "server unreachable"));

        // Act
        var error = ExceptionTranslator.ToFieldError(rpc, "foo");

        // Assert
        error.Path.ShouldBe(["foo"]);
        error.Extensions.ShouldNotBeNull();
        error.Extensions!["code"].ShouldBe("UPSTREAM_ERROR");
        error.Extensions["grpcStatus"].ShouldBe("Unavailable");
        error.Extensions["grpcStatusCode"].ShouldBe((int)StatusCode.Unavailable);
    }

    [Fact]
    public void TopLevelError_omits_path_and_keeps_extensions()
    {
        // Arrange
        var rpc = new RpcException(new Status(StatusCode.Unavailable, "x"));

        // Act
        var error = ExceptionTranslator.ToTopLevelError(rpc);

        // Assert
        error.Path.ShouldBeEmpty();
        error.Extensions.ShouldNotBeNull();
        error.Extensions!["grpcStatusCode"].ShouldBe((int)StatusCode.Unavailable);
    }

    [Theory]
    [InlineData(typeof(JsonException), "INVALID_JSON")]
    [InlineData(typeof(ProtocNotFoundException), "PROTOC_NOT_FOUND")]
    [InlineData(typeof(FileNotFoundException), "FILE_NOT_FOUND")]
    [InlineData(typeof(HttpRequestException), "CONNECTION_FAILED")]
    [InlineData(typeof(TimeoutException), "TIMEOUT")]
    [InlineData(typeof(InvalidOperationException), "INTERNAL_ERROR")]
    public void NonRpc_exceptions_carry_category_code_in_extensions(Type exceptionType, string expectedCode)
    {
        // Arrange
        var ex = (Exception)Activator.CreateInstance(exceptionType, "boom")!;

        // Act
        var error = ExceptionTranslator.ToTopLevelError(ex);

        // Assert
        error.Extensions.ShouldNotBeNull();
        error.Extensions!["code"].ShouldBe(expectedCode);
    }

    [Fact]
    public void Cancellation_translates_to_cancelled_code()
    {
        // Arrange

        // Act
        var error = ExceptionTranslator.ToTopLevelError(new OperationCanceledException());

        // Assert
        error.Extensions.ShouldNotBeNull();
        error.Extensions!["code"].ShouldBe("CANCELLED");
    }

    [Theory]
    [InlineData(typeof(OperationCanceledException), 130)]
    [InlineData(typeof(JsonException), 2)]
    [InlineData(typeof(ProtocNotFoundException), 3)]
    [InlineData(typeof(FileNotFoundException), 3)]
    [InlineData(typeof(HttpRequestException), 4)]
    [InlineData(typeof(TimeoutException), 5)]
    [InlineData(typeof(InvalidOperationException), 1)]
    public void ExitCodeFor_maps_categories(Type exceptionType, int expectedExitCode)
    {
        // Arrange

        // Act
        var ex = (Exception)Activator.CreateInstance(exceptionType, "x")!;

        // Assert
        ExceptionTranslator.ExitCodeFor(ex).ShouldBe(expectedExitCode);
    }

    [Fact]
    public void Rpc_error_exit_code_is_64_plus_status()
    {
        // Arrange

        // Act
        var rpc = new RpcException(new Status(StatusCode.InvalidArgument, "x"));

        // Assert
        ExceptionTranslator.ExitCodeFor(rpc).ShouldBe(64 + (int)StatusCode.InvalidArgument);
    }
}
