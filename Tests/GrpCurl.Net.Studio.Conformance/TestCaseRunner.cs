using Connectrpc.Conformance.V1;
using Google.Protobuf;
using Google.Protobuf.Reflection;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Grpc.Net.Client;
using GrpCurl.Net.Invocation;
using GrpCurl.Net.Studio.ViewModels.Services;
using GrpCurl.Net.Utilities;
using System.Security.Cryptography.X509Certificates;

namespace GrpCurl.Net.Studio.Conformance;

/// <summary>
///     Executes one <see cref="ClientCompatRequest" /> by driving the Studio
///     <see cref="IInvocationService" /> — the exact invoke path the app uses — so a passing suite
///     certifies the desktop application's invocation path. Unary and server-streaming are routed
///     here (E2.1 PR-A); client/duplex arrive with PR-B. Channel, method resolution, header parsing,
///     and request unpacking mirror the CLI adapter; only the invoke calls differ.
/// </summary>
internal static class TestCaseRunner
{
    private const string DefaultServiceName = "connectrpc.conformance.v1.ConformanceService";

    private static readonly IInvocationService Invocation = new InvocationService();

    public static async Task<ClientResponseResult> RunAsync(ClientCompatRequest request)
    {
        Validate(request);

        var method = ResolveMethod(request);

        using var channel = CreateChannel(request);
        using var cts = new CancellationTokenSource();

        var metadata = BuildMetadata(request);

        DateTime? deadline = request.HasTimeoutMs
            ? DateTime.UtcNow.AddMilliseconds(request.TimeoutMs)
            : null;

        var context = new StreamContext();
        ScheduleAfterCloseSendCancellation(request, context, cts);

        return request.StreamType switch
        {
            StreamType.Unary or StreamType.Unspecified => await RunUnaryAsync(request, method, channel, metadata, deadline, context, cts),
            StreamType.ServerStream => await RunServerStreamAsync(request, method, channel, metadata, deadline, context, cts),
            _ => throw new ArgumentException(
                $"The Studio conformance adapter does not yet support '{request.StreamType}' (client/duplex arrive with E2.1 PR-B).")
        };
    }

    private static async Task<ClientResponseResult> RunUnaryAsync(
        ClientCompatRequest request, MethodDescriptor method, GrpcChannel channel,
        Metadata metadata, DateTime? deadline, StreamContext context, CancellationTokenSource cts)
    {
        var message = SingleRequestMessage(request, method);

        if (request.RequestDelayMs > 0)
        {
            await Task.Delay((int)request.RequestDelayMs);
        }

        // The single request is sent implicitly when the call is issued, which closes the send side.
        context.AllSent.TrySetResult();

        var result = new ClientResponseResult();

        try
        {
            var outcome = await Invocation.InvokeUnaryAsync(channel, method, message, metadata, deadline, cts.Token);

            if (outcome.Ok)
            {
                ResultBuilder.AddHeaders(result.ResponseHeaders, outcome.ResponseHeaders);
                ResultBuilder.AddPayload(result.Payloads, outcome.Response!, method);
                ResultBuilder.AddHeaders(result.ResponseTrailers, outcome.ResponseTrailers);
            }
            else
            {
                ResultBuilder.ApplyError(result, Reconstruct(outcome.Status, outcome.ResponseTrailers));

                if (result.ResponseHeaders.Count == 0)
                {
                    ResultBuilder.AddHeaders(result.ResponseHeaders, outcome.ResponseHeaders);
                }
            }
        }
        catch (OperationCanceledException)
        {
            ResultBuilder.ApplyCanceled(result);
        }

        return result;
    }

    private static async Task<ClientResponseResult> RunServerStreamAsync(
        ClientCompatRequest request, MethodDescriptor method, GrpcChannel channel,
        Metadata metadata, DateTime? deadline, StreamContext context, CancellationTokenSource cts)
    {
        var message = SingleRequestMessage(request, method);

        if (request.RequestDelayMs > 0)
        {
            await Task.Delay((int)request.RequestDelayMs);
        }

        context.AllSent.TrySetResult();

        var result = new ClientResponseResult();
        var afterNumResponses = AfterNumResponses(request);
        var received = 0u;

        try
        {
            await foreach (var ev in Invocation.InvokeStreamingAsync(channel, method, Single(message), metadata, deadline, cts.Token))
            {
                switch (ev)
                {
                    case HeadersReceived headers:
                        ResultBuilder.AddHeaders(result.ResponseHeaders, headers.Headers);
                        break;

                    case MessageReceived msg:
                        ResultBuilder.AddPayload(result.Payloads, msg.Message, method);
                        received++;
                        if (afterNumResponses > 0 && received >= afterNumResponses)
                        {
                            cts.Cancel();
                        }

                        break;

                    case StatusReceived status:
                        if (status.Status.Code == 0)
                        {
                            ResultBuilder.AddHeaders(result.ResponseTrailers, status.Trailers);
                        }
                        else
                        {
                            ResultBuilder.ApplyError(result, Reconstruct(status.Status, status.Trailers));
                        }

                        break;
                }
            }
        }
        catch (OperationCanceledException)
        {
            ResultBuilder.ApplyCanceled(result);
        }

        return result;
    }

    // Reconstruct an RpcException carrying the captured trailers so the shared ResultBuilder decodes
    // rich google.rpc.Status details identically to the CLI.
    private static RpcException Reconstruct(InvocationStatus status, Metadata? trailers)
        => new(new Status((StatusCode)status.Code, status.Detail), trailers ?? []);

    private static async IAsyncEnumerable<IMessage> Single(IMessage message)
    {
        yield return message;
        await Task.CompletedTask;
    }

    private static void Validate(ClientCompatRequest request)
    {
        if (request.Protocol != Protocol.Grpc)
        {
            throw new ArgumentException($"Unsupported protocol '{request.Protocol}'. This adapter only speaks gRPC.");
        }

        if (request.HttpVersion is not (HTTPVersion._2 or HTTPVersion.Unspecified))
        {
            throw new ArgumentException($"Unsupported HTTP version '{request.HttpVersion}'. gRPC requires HTTP/2.");
        }

        if (request.Codec is not (Codec.Proto or Codec.Unspecified))
        {
            throw new ArgumentException($"Unsupported codec '{request.Codec}'. Only binary proto is supported.");
        }

        if (request.Compression is not (Compression.Identity or Compression.Gzip or Compression.Unspecified))
        {
            throw new ArgumentException($"Unsupported compression '{request.Compression}'. Only identity and gzip are supported.");
        }

        if (request.RawRequest is not null)
        {
            throw new ArgumentException("raw_request is only supported by the reference client.");
        }

        if (request.UseGetHttpMethod)
        {
            throw new ArgumentException("HTTP GET requests are only relevant to the Connect protocol.");
        }
    }

    private static MethodDescriptor ResolveMethod(ClientCompatRequest request)
    {
        var serviceName = request.HasService ? request.Service : DefaultServiceName;

        var service = ServiceReflection.Descriptor.Services.FirstOrDefault(s => s.FullName == serviceName)
                      ?? throw new ArgumentException($"Unknown service '{serviceName}'.");

        if (!request.HasMethod)
        {
            throw new ArgumentException("No method specified in the request.");
        }

        return service.FindMethodByName(request.Method)
               ?? throw new ArgumentException($"Unknown method '{serviceName}/{request.Method}'.");
    }

    private static GrpcChannel CreateChannel(ClientCompatRequest request)
    {
        var useTls = !request.ServerTlsCert.IsEmpty;

        var options = new GrpcChannelFactory.ChannelOptions
        {
            Plaintext = !useTls,
            CaCertPem = useTls ? request.ServerTlsCert.ToStringUtf8() : null,
            ClientCertPem = request.ClientTlsCreds is { Cert.IsEmpty: false }
                ? request.ClientTlsCreds.Cert.ToStringUtf8()
                : null,
            ClientKeyPem = request.ClientTlsCreds is { Key.IsEmpty: false }
                ? request.ClientTlsCreds.Key.ToStringUtf8()
                : null,
            RevocationMode = useTls ? X509RevocationMode.NoCheck : null,
            MaxReceiveMessageSize = request.MessageReceiveLimit > 0 ? (int)request.MessageReceiveLimit : null
        };

        return GrpcChannelFactory.Create($"{request.Host}:{request.Port}", options);
    }

    private static Metadata BuildMetadata(ClientCompatRequest request)
    {
        var headerStrings = new List<string>();

        foreach (var header in request.RequestHeaders)
        {
            foreach (var value in header.Value)
            {
                headerStrings.Add($"{header.Name}: {value}");
            }
        }

        var metadata = GrpcChannelFactory.CreateMetadata(headerStrings);

        if (request.Compression == Compression.Gzip)
        {
            metadata.Add("grpc-internal-encoding-request", "gzip");
        }

        return metadata;
    }

    private static void ScheduleAfterCloseSendCancellation(ClientCompatRequest request, StreamContext context, CancellationTokenSource cts)
    {
        if (request.Cancel is null)
        {
            return;
        }

        var delayMs = request.Cancel.CancelTimingCase switch
        {
            ClientCompatRequest.Types.Cancel.CancelTimingOneofCase.AfterCloseSendMs => (int)request.Cancel.AfterCloseSendMs,
            ClientCompatRequest.Types.Cancel.CancelTimingOneofCase.None => 0,
            _ => -1
        };

        if (delayMs < 0)
        {
            return;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                await context.AllSent.Task.ConfigureAwait(false);
                await Task.Delay(delayMs).ConfigureAwait(false);
                cts.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // The test finished and disposed the CTS before the cancel fired.
            }
        });
    }

    private static uint AfterNumResponses(ClientCompatRequest request) =>
        request.Cancel?.CancelTimingCase == ClientCompatRequest.Types.Cancel.CancelTimingOneofCase.AfterNumResponses
            ? request.Cancel.AfterNumResponses
            : 0;

    private static IMessage SingleRequestMessage(ClientCompatRequest request, MethodDescriptor method)
    {
        if (request.RequestMessages.Count != 1)
        {
            throw new ArgumentException(
                $"This shape requires exactly one request message, got {request.RequestMessages.Count}.");
        }

        return UnpackToDynamic(request.RequestMessages[0], method.InputType);
    }

    private static IMessage UnpackToDynamic(Any any, MessageDescriptor expected)
    {
        var typeUrl = any.TypeUrl;
        var typeName = typeUrl.Contains('/') ? typeUrl[(typeUrl.LastIndexOf('/') + 1)..] : typeUrl;

        if (typeName != expected.FullName)
        {
            throw new ArgumentException(
                $"Request message type '{typeName}' does not match the method input type '{expected.FullName}'.");
        }

        var message = new SimpleDynamicMessage(expected);

        using var input = new CodedInputStream(any.Value.ToByteArray());

        message.MergeFrom(input);

        return message;
    }

    /// <summary>Streaming bookkeeping (PR-A: send-close signalling; the full-duplex gates land in PR-B).</summary>
    private sealed class StreamContext
    {
        public readonly TaskCompletionSource AllSent = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
