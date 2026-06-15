using Google.Protobuf;
using Google.Protobuf.Reflection;
using Grpc.Core;
using Grpc.Net.Client;
using GrpCurl.Net.Invocation;

namespace GrpCurl.Net.Studio.ViewModels.Services;

/// <inheritdoc cref="IInvocationService" />
public sealed class InvocationService : IInvocationService
{
    public async Task<UnaryOutcome> InvokeUnaryAsync(
        GrpcChannel channel,
        MethodDescriptor method,
        IMessage request,
        Metadata headers,
        DateTime? deadline,
        CancellationToken cancellationToken)
    {
        var invoker = new DynamicInvoker(channel);

        try
        {
            var result = await invoker.InvokeUnaryAsync(method, request, headers, deadline, cancellationToken)
                .ConfigureAwait(false);

            return new UnaryOutcome(
                Ok: true,
                ResponseHeaders: result.ResponseHeaders ?? [],
                Response: result.Response,
                ResponseTrailers: result.ResponseTrailers,
                Status: new InvocationStatus((int)StatusCode.OK, nameof(StatusCode.OK), string.Empty));
        }
        catch (RpcInvocationException ex)
        {
            // Failure that still produced response headers (e.g. server set metadata then errored).
            return new UnaryOutcome(false, ex.ResponseHeaders, Response: null, ex.Trailers, ToStatus(ex.Status));
        }
        catch (RpcException ex)
        {
            return new UnaryOutcome(false, [], Response: null, ex.Trailers, ToStatus(ex.Status));
        }
    }

    public IMessage CreateMessageFromJson(MessageDescriptor descriptor, string? json, bool allowUnknownFields = true)
        => DynamicInvoker.CreateMessageFromJson(descriptor, json, allowUnknownFields);

    public string MessageToJson(IMessage message, bool includeDefaults = false, bool indent = true)
        => DynamicInvoker.MessageToJson(message, includeDefaults, indent);

    private static InvocationStatus ToStatus(Status status)
        => new((int)status.StatusCode, status.StatusCode.ToString(), status.Detail);
}
