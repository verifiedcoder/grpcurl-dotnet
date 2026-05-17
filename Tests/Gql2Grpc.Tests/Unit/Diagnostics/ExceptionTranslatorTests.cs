using System.Text.Json;
using Gql2Grpc.Diagnostics;
using Grpc.Core;

namespace Gql2Grpc.Tests.Unit.Diagnostics;

public sealed class ExceptionTranslatorTests
{
    [Fact]
    public void RpcException_translates_to_grpc_extensions_with_field_path()
    {
        var rpc = new RpcException(new Status(StatusCode.Unavailable, "server unreachable"));
        var error = ExceptionTranslator.ToFieldError(rpc, "foo");

        error.Path.ShouldBe(["foo"]);
        error.Extensions.ShouldNotBeNull();
        error.Extensions!["code"].ShouldBe("UPSTREAM_ERROR");
        error.Extensions["grpcStatus"].ShouldBe("Unavailable");
        error.Extensions["grpcStatusCode"].ShouldBe((int)StatusCode.Unavailable);
    }

    [Fact]
    public void TopLevelError_omits_path_and_keeps_extensions()
    {
        var rpc = new RpcException(new Status(StatusCode.Unavailable, "x"));
        var error = ExceptionTranslator.ToTopLevelError(rpc);

        error.Path.ShouldBeEmpty();
        error.Extensions.ShouldNotBeNull();
        error.Extensions!["grpcStatusCode"].ShouldBe((int)StatusCode.Unavailable);
    }

    [Theory]
    [InlineData(typeof(JsonException), "INVALID_JSON")]
    [InlineData(typeof(FileNotFoundException), "FILE_NOT_FOUND")]
    [InlineData(typeof(HttpRequestException), "CONNECTION_FAILED")]
    [InlineData(typeof(TimeoutException), "TIMEOUT")]
    [InlineData(typeof(InvalidOperationException), "INTERNAL_ERROR")]
    public void NonRpc_exceptions_carry_category_code_in_extensions(Type exceptionType, string expectedCode)
    {
        var ex = (Exception)Activator.CreateInstance(exceptionType, "boom")!;
        var error = ExceptionTranslator.ToTopLevelError(ex);

        error.Extensions.ShouldNotBeNull();
        error.Extensions!["code"].ShouldBe(expectedCode);
    }

    [Fact]
    public void Cancellation_translates_to_cancelled_code()
    {
        var error = ExceptionTranslator.ToTopLevelError(new OperationCanceledException());

        error.Extensions.ShouldNotBeNull();
        error.Extensions!["code"].ShouldBe("CANCELLED");
    }

    [Theory]
    [InlineData(typeof(OperationCanceledException), 130)]
    [InlineData(typeof(JsonException), 2)]
    [InlineData(typeof(FileNotFoundException), 3)]
    [InlineData(typeof(HttpRequestException), 4)]
    [InlineData(typeof(TimeoutException), 5)]
    [InlineData(typeof(InvalidOperationException), 1)]
    public void ExitCodeFor_maps_categories(Type exceptionType, int expectedExitCode)
    {
        var ex = (Exception)Activator.CreateInstance(exceptionType, "x")!;
        ExceptionTranslator.ExitCodeFor(ex).ShouldBe(expectedExitCode);
    }

    [Fact]
    public void Rpc_error_exit_code_is_64_plus_status()
    {
        var rpc = new RpcException(new Status(StatusCode.InvalidArgument, "x"));
        ExceptionTranslator.ExitCodeFor(rpc).ShouldBe(64 + (int)StatusCode.InvalidArgument);
    }
}
