using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
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
            return new UnaryOutcome(false, ex.ResponseHeaders, Response: null, ex.Trailers, ToStatus(ex.Status), RichStatusDecoder.TryDecode(ex));
        }
        catch (RpcException ex)
        {
            return new UnaryOutcome(false, [], Response: null, ex.Trailers, ToStatus(ex.Status), RichStatusDecoder.TryDecode(ex));
        }
    }

    public async IAsyncEnumerable<StreamEvent> InvokeStreamingAsync(
        GrpcChannel channel,
        MethodDescriptor method,
        IAsyncEnumerable<IMessage> requests,
        Metadata headers,
        DateTime? deadline,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var invoker = new DynamicInvoker(channel);
        var clock = Stopwatch.StartNew();

        // Bounded + Wait so a saturated consumer (UI pump / conformance loop) backpressures into
        // HTTP/2 flow control rather than buffering unboundedly (ADR-013 / SPEC-030 §6).
        var events = Channel.CreateBounded<StreamEvent>(new BoundedChannelOptions(1000)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false
        });

        // Linked so the consumer breaking out early (or a tab close) unblocks a producer that's
        // parked on a full-channel WriteAsync (ADR-013 backpressure / ADR-014 per-tab CTS).
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        var producer = Task.Run(
            () => ProduceAsync(invoker, method, requests, headers, deadline, events.Writer, clock, linked.Token));

        try
        {
            // No token here: drain the channel to its completion. Cancellation faults the channel
            // (via the producer), which ReadAllAsync re-throws — so already-yielded events are kept.
            await foreach (var ev in events.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            {
                yield return ev;
            }
        }
        finally
        {
            linked.Cancel();

            try
            {
                await producer.ConfigureAwait(false);
            }
            catch
            {
                // The producer only completes the channel; failures already reached the consumer.
            }
        }
    }

    private static async Task ProduceAsync(
        DynamicInvoker invoker,
        MethodDescriptor method,
        IAsyncEnumerable<IMessage> requests,
        Metadata headers,
        DateTime? deadline,
        ChannelWriter<StreamEvent> writer,
        Stopwatch clock,
        CancellationToken cancellationToken)
    {
        try
        {
            await DispatchAsync(invoker, method, requests, headers, deadline, writer, clock, cancellationToken).ConfigureAwait(false);
            writer.TryComplete();
        }
        catch (Exception ex)
        {
            // Cancellation (and any unexpected fault) faults the channel so the consumer observes it
            // — mirroring unary, where cancellation propagates and the VM records it. Already-emitted
            // received events were drained before this point (cancel-preserves-received, FR-084).
            writer.TryComplete(ex);
        }
    }

    private static async Task DispatchAsync(
        DynamicInvoker invoker, MethodDescriptor method, IAsyncEnumerable<IMessage> requests,
        Metadata headers, DateTime? deadline, ChannelWriter<StreamEvent> writer, Stopwatch clock, CancellationToken ct)
    {
        try
        {
            if (method.IsClientStreaming && method.IsServerStreaming)
            {
                await ProduceDuplexAsync(invoker, method, WrapSends(requests, writer, clock, ct), headers, deadline, writer, clock, ct).ConfigureAwait(false);
            }
            else if (method.IsClientStreaming)
            {
                await ProduceClientStreamingAsync(invoker, method, WrapSends(requests, writer, clock, ct), headers, deadline, writer, clock, ct).ConfigureAwait(false);
            }
            else
            {
                await ProduceServerStreamingAsync(invoker, method, requests, headers, deadline, writer, clock, ct).ConfigureAwait(false);
            }
        }
        catch (RpcInvocationException ex)
        {
            // Errors that surface before headers are readable (client-streaming) carry them.
            if (ex.ResponseHeaders.Count > 0)
            {
                await EmitAsync(writer, new HeadersReceived(ex.ResponseHeaders), clock, ct).ConfigureAwait(false);
            }

            await EmitAsync(writer, ToTerminal(ex), clock, ct).ConfigureAwait(false);
        }
        catch (RpcException ex)
        {
            await EmitAsync(writer, ToTerminal(ex), clock, ct).ConfigureAwait(false);
        }
    }

    private static async Task ProduceServerStreamingAsync(
        DynamicInvoker invoker, MethodDescriptor method, IAsyncEnumerable<IMessage> requests,
        Metadata headers, DateTime? deadline, ChannelWriter<StreamEvent> writer, Stopwatch clock, CancellationToken ct)
    {
        var request = await FirstAsync(requests, ct).ConfigureAwait(false);

        using var streaming = invoker.InvokeServerStreamingWithMetadataAsync(method, request, headers, deadline, ct);

        await EmitAsync(writer, new HeadersReceived(await streaming.ResponseHeadersAsync.ConfigureAwait(false)), clock, ct).ConfigureAwait(false);

        long index = 0;
        await foreach (var message in streaming.ResponseStream.WithCancellation(ct).ConfigureAwait(false))
        {
            await EmitAsync(writer, new MessageReceived(index++, message), clock, ct).ConfigureAwait(false);
        }

        await EmitAsync(writer, OkStatus(streaming.GetTrailers()), clock, ct).ConfigureAwait(false);
    }

    private static async Task ProduceClientStreamingAsync(
        DynamicInvoker invoker, MethodDescriptor method, IAsyncEnumerable<IMessage> requests,
        Metadata headers, DateTime? deadline, ChannelWriter<StreamEvent> writer, Stopwatch clock, CancellationToken ct)
    {
        using var result = await invoker.InvokeClientStreamingWithMetadataAsync(method, requests, headers, deadline, ct).ConfigureAwait(false);

        await EmitAsync(writer, new HeadersReceived(await result.ResponseHeadersAsync.ConfigureAwait(false)), clock, ct).ConfigureAwait(false);
        await EmitAsync(writer, new MessageReceived(0, result.Response), clock, ct).ConfigureAwait(false);
        await EmitAsync(writer, OkStatus(result.GetTrailers()), clock, ct).ConfigureAwait(false);
    }

    private static async Task ProduceDuplexAsync(
        DynamicInvoker invoker, MethodDescriptor method, IAsyncEnumerable<IMessage> requests,
        Metadata headers, DateTime? deadline, ChannelWriter<StreamEvent> writer, Stopwatch clock, CancellationToken ct)
    {
        using var streaming = invoker.InvokeDuplexStreamingWithMetadataAsync(method, requests, headers, deadline, ct);

        await EmitAsync(writer, new HeadersReceived(await streaming.ResponseHeadersAsync.ConfigureAwait(false)), clock, ct).ConfigureAwait(false);

        long index = 0;
        await foreach (var message in streaming.ResponseStream.WithCancellation(ct).ConfigureAwait(false))
        {
            await EmitAsync(writer, new MessageReceived(index++, message), clock, ct).ConfigureAwait(false);
        }

        await EmitAsync(writer, OkStatus(streaming.GetTrailers()), clock, ct).ConfigureAwait(false);
    }

    // Wraps the caller's requests so each pulled message emits a MessageSent on the same channel,
    // interleaving chronologically with received events (the merged-order property duplex needs).
    private static async IAsyncEnumerable<IMessage> WrapSends(
        IAsyncEnumerable<IMessage> requests, ChannelWriter<StreamEvent> writer, Stopwatch clock,
        [EnumeratorCancellation] CancellationToken ct)
    {
        long index = 0;
        await foreach (var message in requests.WithCancellation(ct).ConfigureAwait(false))
        {
            yield return message;
            await EmitAsync(writer, new MessageSent(index++, message), clock, ct).ConfigureAwait(false);
        }
    }

    private static async Task<IMessage> FirstAsync(IAsyncEnumerable<IMessage> source, CancellationToken ct)
    {
        await foreach (var message in source.WithCancellation(ct).ConfigureAwait(false))
        {
            return message;
        }

        throw new InvalidOperationException("A server-streaming call requires exactly one request message.");
    }

    private static ValueTask EmitAsync(ChannelWriter<StreamEvent> writer, StreamEvent ev, Stopwatch clock, CancellationToken ct)
        => writer.WriteAsync(ev with { ElapsedMs = clock.ElapsedMilliseconds, WallClock = DateTimeOffset.UtcNow }, ct);

    private static StreamEvent OkStatus(Metadata? trailers)
        => new StatusReceived(new InvocationStatus((int)StatusCode.OK, nameof(StatusCode.OK), string.Empty), trailers);

    private static StreamEvent ToTerminal(RpcException ex)
        => new StatusReceived(ToStatus(ex.Status), ex.Trailers, RichStatusDecoder.TryDecode(ex));

    public IMessage CreateMessageFromJson(MessageDescriptor descriptor, string? json, bool allowUnknownFields = true)
        => DynamicInvoker.CreateMessageFromJson(descriptor, json, allowUnknownFields);

    public IMessage CreateMessageFromText(MessageDescriptor descriptor, string? text)
        => DynamicTextFormat.Parse(descriptor, text ?? string.Empty);

    public string MessageToJson(IMessage message, bool includeDefaults = false, bool indent = true)
        => DynamicInvoker.MessageToJson(message, includeDefaults, indent);

    private static InvocationStatus ToStatus(Status status)
        => new((int)status.StatusCode, status.StatusCode.ToString(), status.Detail);
}
