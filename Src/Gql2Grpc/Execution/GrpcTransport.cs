using System.Runtime.CompilerServices;
using Google.Protobuf.Reflection;
using Grpc.Core;
using Grpc.Net.Client;
using GrpCurl.Net.Invocation;

namespace Gql2Grpc.Execution;

/// <summary>
/// Thin adapter over <see cref="DynamicInvoker"/> that works in JSON string terms rather than
/// raw <c>IMessage</c> types. The GraphQL pipeline only speaks JSON, so this boundary keeps
/// protobuf types off of the rest of Gql2Grpc.
/// </summary>
internal sealed class GrpcTransport(GrpcChannel channel)
{
    private readonly DynamicInvoker _invoker = new(channel);

    public async Task<string> InvokeUnaryAsync(
        MethodDescriptor method,
        string requestJson,
        Metadata metadata,
        DateTime? deadline,
        bool emitDefaults,
        bool allowUnknownFields,
        CancellationToken cancellationToken)
    {
        var request = DynamicInvoker.CreateMessageFromJson(method.InputType, requestJson, allowUnknownFields);
        var result = await _invoker.InvokeUnaryAsync(method, request, metadata, deadline, cancellationToken).ConfigureAwait(false);
        return DynamicInvoker.MessageToJson(result.Response, emitDefaults);
    }

    public async IAsyncEnumerable<string> InvokeServerStreamingAsync(
        MethodDescriptor method,
        string requestJson,
        Metadata metadata,
        DateTime? deadline,
        bool emitDefaults,
        bool allowUnknownFields,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var request = DynamicInvoker.CreateMessageFromJson(method.InputType, requestJson, allowUnknownFields);

        await foreach (var response in _invoker.InvokeServerStreamingAsync(method, request, metadata, deadline, cancellationToken).ConfigureAwait(false))
        {
            yield return DynamicInvoker.MessageToJson(response, emitDefaults);
        }
    }
}
