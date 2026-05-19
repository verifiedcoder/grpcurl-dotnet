using Gql2Grpc.GraphQL;
using Grpc.Core;
using System.Text.Json;

namespace Gql2Grpc.Diagnostics;

/// <summary>
///     Converts common failure exceptions into <see cref="GraphQLError" /> values (for the response
///     envelope) and exit codes matching <c>GrpCurl.Net</c>'s convention.
/// </summary>
/// <remarks>
///     Field-level callers (the executor) use <see cref="ToFieldError" /> to attach the error to a
///     specific GraphQL response key. Command-level callers (the SetAction catch chain) use
///     <see cref="ToTopLevelError" />, which omits the <c>path</c> entirely.
///     Extensions (<c>code</c>, and for RPC errors <c>grpcStatus</c>/<c>grpcStatusCode</c>) are always
///     emitted — there is no opt-out.
/// </remarks>
internal static class ExceptionTranslator
{
    private const int CommandFailedExitCode = 1;
    private const int UsageExitCode = 2;
    private const int SchemaExitCode = 3;
    private const int NetworkExitCode = 4;
    private const int TimeoutExitCode = 5;
    private const int CanceledExitCode = 130;
    private const int GrpcExitCodeBase = 64;

    public static GraphQLError ToFieldError(Exception exception, string responseKey)
        => Build(exception, [responseKey]);

    public static GraphQLError ToTopLevelError(Exception exception)
        => Build(exception, []);

    public static int ExitCodeFor(Exception exception)
        => exception switch
        {
            OperationCanceledException                          => CanceledExitCode,
            RpcException rpc                                    => GrpcExitCodeBase + (int)rpc.StatusCode,
            JsonException                                       => UsageExitCode,
            FileNotFoundException or DirectoryNotFoundException => SchemaExitCode,
            HttpRequestException                                => NetworkExitCode,
            TimeoutException                                    => TimeoutExitCode,
            _                                                   => CommandFailedExitCode
        };

    private static GraphQLError Build(Exception exception, IReadOnlyList<object> path)
        => exception switch
        {
            RpcException rpc => FromRpcException(rpc, path),
            JsonException json => new GraphQLError(
                $"Invalid JSON: {json.Message}",
                path,
                new Dictionary<string, object?> { ["code"] = "INVALID_JSON" }),
            FileNotFoundException fnf => new GraphQLError(
                $"Required file not found: {fnf.FileName ?? fnf.Message}",
                path,
                new Dictionary<string, object?> { ["code"] = "FILE_NOT_FOUND" }),
            HttpRequestException http => new GraphQLError(
                $"Upstream connection failed: {http.Message}",
                path,
                new Dictionary<string, object?> { ["code"] = "CONNECTION_FAILED" }),
            TimeoutException timeout => new GraphQLError(
                $"Upstream call timed out: {timeout.Message}",
                path,
                new Dictionary<string, object?> { ["code"] = "TIMEOUT" }),
            OperationCanceledException => new GraphQLError(
                "Operation cancelled",
                path,
                new Dictionary<string, object?> { ["code"] = "CANCELLED" }),
            _ => new GraphQLError(
                exception.Message,
                path,
                new Dictionary<string, object?> { ["code"] = "INTERNAL_ERROR" })
        };

    private static GraphQLError FromRpcException(RpcException rpc, IReadOnlyList<object> path)
    {
        var extensions = new Dictionary<string, object?>
        {
            ["code"] = "UPSTREAM_ERROR",
            ["grpcStatus"] = rpc.StatusCode.ToString(),
            ["grpcStatusCode"] = (int)rpc.StatusCode
        };

        var detail = string.IsNullOrEmpty(rpc.Status.Detail) ? rpc.Message : rpc.Status.Detail;

        return new GraphQLError(detail, path, extensions);
    }
}