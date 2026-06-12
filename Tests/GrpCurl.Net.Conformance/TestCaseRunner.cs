using Connectrpc.Conformance.V1;
using Google.Protobuf;
using Google.Protobuf.Reflection;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using GrpCurl.Net.Invocation;
using GrpCurl.Net.Utilities;
using System.Runtime.CompilerServices;
using System.Security.Cryptography.X509Certificates;

namespace GrpCurl.Net.Conformance;

/// <summary>
///     Executes one <see cref="ClientCompatRequest" /> by driving GrpCurl.Net.Core's own
///     invocation path — <see cref="GrpcChannelFactory" /> for the channel and
///     <see cref="DynamicInvoker" /> with <see cref="SimpleDynamicMessage" /> for the RPC —
///     so a passing suite certifies the product, not the underlying gRPC stack.
/// </summary>
internal static class TestCaseRunner
{
    private const string DefaultServiceName = "connectrpc.conformance.v1.ConformanceService";

    public static async Task<ClientResponseResult> RunAsync(ClientCompatRequest request)
    {
        Validate(request);

        var method = ResolveMethod(request);

        using var channel = CreateChannel(request);
        using var cts = new CancellationTokenSource();

        var invoker = new DynamicInvoker(channel);
        var metadata = BuildMetadata(request);

        DateTime? deadline = request.HasTimeoutMs
            ? DateTime.UtcNow.AddMilliseconds(request.TimeoutMs)
            : null;

        var context = new StreamContext();

        ScheduleAfterCloseSendCancellation(request, context, cts);

        var result = request.StreamType switch
        {
            StreamType.Unary => await RunUnaryAsync(request, method, invoker, metadata, deadline, context, cts),
            StreamType.ServerStream => await RunServerStreamAsync(request, method, invoker, metadata, deadline, context, cts),
            StreamType.ClientStream => await RunClientStreamAsync(request, method, invoker, metadata, deadline, context, cts),
            StreamType.HalfDuplexBidiStream => await RunBidiAsync(request, method, invoker, metadata, deadline, context, cts, fullDuplex: false),
            StreamType.FullDuplexBidiStream => await RunBidiAsync(request, method, invoker, metadata, deadline, context, cts, fullDuplex: true),
            _ => throw new ArgumentException($"Unsupported stream type: {request.StreamType}")
        };

        return result;
    }

    /// <summary>
    ///     Rejects request shapes outside the declared feature matrix (gRPC over HTTP/2,
    ///     proto codec, identity/gzip). The runner only sends what the config declares,
    ///     so a violation here indicates adapter/config drift worth failing loudly on.
    /// </summary>
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

    private static Grpc.Net.Client.GrpcChannel CreateChannel(ClientCompatRequest request)
    {
        var useTls = !request.ServerTlsCert.IsEmpty;

        var options = new GrpcChannelFactory.ChannelOptions
        {
            // No TLS cert means H2C: the factory dials http:// with HTTP/2 prior knowledge.
            Plaintext = !useTls,
            CaCertPem = useTls ? request.ServerTlsCert.ToStringUtf8() : null,
            ClientCertPem = request.ClientTlsCreds is { Cert.IsEmpty: false }
                ? request.ClientTlsCreds.Cert.ToStringUtf8()
                : null,
            ClientKeyPem = request.ClientTlsCreds is { Key.IsEmpty: false }
                ? request.ClientTlsCreds.Key.ToStringUtf8()
                : null,
            // The runner's certificates are ephemeral with no CRL/OCSP endpoints, so the
            // factory's Online default would fail chain building.
            RevocationMode = useTls ? X509RevocationMode.NoCheck : null,
            MaxReceiveMessageSize = request.MessageReceiveLimit > 0 ? (int)request.MessageReceiveLimit : null
        };

        return GrpcChannelFactory.Create($"{request.Host}:{request.Port}", options);
    }

    private static Metadata BuildMetadata(ClientCompatRequest request)
    {
        // Route through the product's own header parser ("name: value" strings, -bin
        // base64 decoding) — the same path the CLI's -H flag uses.
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
            // Grpc.Net.Client special-cases this key: it gzip-compresses request messages
            // and never sends the header itself on the wire.
            metadata.Add("grpc-internal-encoding-request", "gzip");
        }

        return metadata;
    }

    /// <summary>
    ///     Handles the after_close_send_ms cancellation timing, including the
    ///     "Cancel present but no oneof set" default, which means cancel immediately
    ///     after the send side closes. before_close_send and after_num_responses are
    ///     handled inside the request source and the response read loop respectively.
    /// </summary>
    private static void ScheduleAfterCloseSendCancellation(
        ClientCompatRequest request,
        StreamContext context,
        CancellationTokenSource cts)
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

    private static async Task<ClientResponseResult> RunUnaryAsync(
        ClientCompatRequest request,
        MethodDescriptor method,
        DynamicInvoker invoker,
        Metadata metadata,
        DateTime? deadline,
        StreamContext context,
        CancellationTokenSource cts)
    {
        var message = SingleRequestMessage(request, method);

        if (request.RequestDelayMs > 0)
        {
            await Task.Delay((int)request.RequestDelayMs);
        }

        // The single request is sent implicitly by invoking, which also closes the send
        // side — so "after close-send" cancellation is armed from this point.
        context.AllSent.TrySetResult();

        var result = new ClientResponseResult();

        try
        {
            var invocation = await invoker.InvokeUnaryAsync(method, message, metadata, deadline, cts.Token);

            ResultBuilder.AddHeaders(result.ResponseHeaders, invocation.ResponseHeaders);
            ResultBuilder.AddPayload(result.Payloads, invocation.Response, method);
            ResultBuilder.AddHeaders(result.ResponseTrailers, invocation.ResponseTrailers);
        }
        catch (RpcException ex)
        {
            ResultBuilder.ApplyError(result, ex);
        }
        catch (OperationCanceledException)
        {
            ResultBuilder.ApplyCanceled(result);
        }

        return result;
    }

    private static async Task<ClientResponseResult> RunServerStreamAsync(
        ClientCompatRequest request,
        MethodDescriptor method,
        DynamicInvoker invoker,
        Metadata metadata,
        DateTime? deadline,
        StreamContext context,
        CancellationTokenSource cts)
    {
        var message = SingleRequestMessage(request, method);

        if (request.RequestDelayMs > 0)
        {
            await Task.Delay((int)request.RequestDelayMs);
        }

        context.AllSent.TrySetResult();

        var result = new ClientResponseResult();

        try
        {
            using var streaming = invoker.InvokeServerStreamingWithMetadataAsync(method, message, metadata, deadline, cts.Token);

            await ConsumeResponsesAsync(streaming, result, method, AfterNumResponses(request), context, cts);
        }
        catch (RpcException ex)
        {
            ResultBuilder.ApplyError(result, ex);
        }
        catch (OperationCanceledException)
        {
            ResultBuilder.ApplyCanceled(result);
        }

        return result;
    }

    private static async Task<ClientResponseResult> RunClientStreamAsync(
        ClientCompatRequest request,
        MethodDescriptor method,
        DynamicInvoker invoker,
        Metadata metadata,
        DateTime? deadline,
        StreamContext context,
        CancellationTokenSource cts)
    {
        var result = new ClientResponseResult();
        var source = BuildRequestSource(request, method, context, cts, fullDuplex: false);

        try
        {
            using var invocation = await invoker.InvokeClientStreamingWithMetadataAsync(method, source, metadata, deadline, cts.Token);

            Metadata? headers = null;

            try
            {
                headers = await invocation.ResponseHeadersAsync;
            }
            catch
            {
                // Headers task faults when the RPC errors; the error surfaced already.
            }

            ResultBuilder.AddHeaders(result.ResponseHeaders, headers);
            ResultBuilder.AddPayload(result.Payloads, invocation.Response, method);
            ResultBuilder.AddHeaders(result.ResponseTrailers, invocation.GetTrailers());
        }
        catch (RpcException ex)
        {
            ResultBuilder.ApplyError(result, ex);
        }
        catch (OperationCanceledException)
        {
            ResultBuilder.ApplyCanceled(result);
        }

        result.NumUnsentRequests = request.RequestMessages.Count - Volatile.Read(ref context.Sent);

        return result;
    }

    private static async Task<ClientResponseResult> RunBidiAsync(
        ClientCompatRequest request,
        MethodDescriptor method,
        DynamicInvoker invoker,
        Metadata metadata,
        DateTime? deadline,
        StreamContext context,
        CancellationTokenSource cts,
        bool fullDuplex)
    {
        var result = new ClientResponseResult();
        var source = BuildRequestSource(request, method, context, cts, fullDuplex);

        try
        {
            using var streaming = invoker.InvokeDuplexStreamingWithMetadataAsync(method, source, metadata, deadline, cts.Token);

            await ConsumeResponsesAsync(streaming, result, method, AfterNumResponses(request), context, cts);
        }
        catch (RpcException ex)
        {
            ResultBuilder.ApplyError(result, ex);
        }
        catch (OperationCanceledException)
        {
            ResultBuilder.ApplyCanceled(result);
        }

        result.NumUnsentRequests = request.RequestMessages.Count - Volatile.Read(ref context.Sent);

        return result;
    }

    /// <summary>
    ///     Shared response-side loop for server-streaming and bidi calls: capture headers
    ///     before iterating (so they survive mid-stream errors), collect payloads, honour
    ///     after_num_responses cancellation, release the full-duplex gate, and read
    ///     trailers once the stream completes.
    /// </summary>
    private static async Task ConsumeResponsesAsync(
        StreamingInvocationResult streaming,
        ClientResponseResult result,
        MethodDescriptor method,
        uint afterNumResponses,
        StreamContext context,
        CancellationTokenSource cts)
    {
        try
        {
            Metadata? headers = null;

            try
            {
                headers = await streaming.ResponseHeadersAsync;
            }
            catch
            {
                // Headers task faults on trailers-only errors; the read loop below
                // observes the same error and maps it.
            }

            ResultBuilder.AddHeaders(result.ResponseHeaders, headers);

            var received = 0u;

            await foreach (var message in streaming.ResponseStream)
            {
                ResultBuilder.AddPayload(result.Payloads, message, method);

                received++;
                context.ResponseGate.Release();

                if (afterNumResponses > 0 && received >= afterNumResponses)
                {
                    cts.Cancel();
                }
            }

            ResultBuilder.AddHeaders(result.ResponseTrailers, streaming.GetTrailers());
        }
        catch (RpcException ex)
        {
            ResultBuilder.ApplyError(result, ex);
        }
        catch (OperationCanceledException)
        {
            ResultBuilder.ApplyCanceled(result);
        }
        finally
        {
            // Unblock a full-duplex request source that may still be gated; it observes
            // StreamDone and stops sending.
            context.StreamDone = true;
            context.ResponseGate.Release(1_000_000);
        }
    }

    /// <summary>
    ///     The request stream is the control surface for send-side semantics: per-message
    ///     delay, full-duplex interleaving (wait for response N-1 before sending N), the
    ///     sent-message count behind num_unsent_requests, and before_close_send
    ///     cancellation (cancel *instead of* closing the send side).
    /// </summary>
    private static async IAsyncEnumerable<IMessage> BuildRequestSource(
        ClientCompatRequest request,
        MethodDescriptor method,
        StreamContext context,
        CancellationTokenSource cts,
        bool fullDuplex,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        foreach (var any in request.RequestMessages)
        {
            if (context.StreamDone)
            {
                // The response stream ended (or errored) — stop sending; the remaining
                // messages count as unsent.
                yield break;
            }

            if (request.RequestDelayMs > 0)
            {
                await Task.Delay((int)request.RequestDelayMs, cancellationToken);
            }

            yield return UnpackToDynamic(any, method.InputType);

            // Post-yield code only runs after the consumer's WriteAsync succeeded and it
            // pulled the next message, so this counts confirmed-written messages.
            Interlocked.Increment(ref context.Sent);

            if (fullDuplex)
            {
                // Interleave request/response pairs: wait for this request's response
                // before sending the next one — and before any close-send/cancel below,
                // so the final response is read before the call is torn down.
                await context.ResponseGate.WaitAsync(cancellationToken);
            }
        }

        if (request.Cancel?.CancelTimingCase == ClientCompatRequest.Types.Cancel.CancelTimingOneofCase.BeforeCloseSend)
        {
            // Cancel instead of close-send: trip the call's token, then hold the
            // enumerable open so the invoker never reaches CompleteAsync. The cancelled
            // token unblocks the delay immediately.
            cts.Cancel();

            await Task.Delay(Timeout.Infinite, cancellationToken);
        }

        context.AllSent.TrySetResult();
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
                $"{request.StreamType} requires exactly one request message, got {request.RequestMessages.Count}.");
        }

        return UnpackToDynamic(request.RequestMessages[0], method.InputType);
    }

    /// <summary>
    ///     Converts an <see cref="Any" />-wrapped request into the product's own
    ///     <see cref="SimpleDynamicMessage" /> so the request bytes round-trip through
    ///     GrpCurl.Net's ProtobufReader/ProtobufWriter on their way to the wire.
    /// </summary>
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
}

/// <summary>
///     Per-test coordination state shared between the request source, the response read
///     loop, and the cancellation scheduler.
/// </summary>
internal sealed class StreamContext
{
    /// <summary>Number of request messages confirmed written to the call.</summary>
    public int Sent;

    /// <summary>Completed when the send side has finished (arms after_close_send_ms).</summary>
    public readonly TaskCompletionSource AllSent = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>Full-duplex interleave gate: one release per received response.</summary>
    public readonly SemaphoreSlim ResponseGate = new(0);

    /// <summary>Set when the response stream has completed or errored.</summary>
    public volatile bool StreamDone;
}
