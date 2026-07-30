using Google.Protobuf;
using Google.Protobuf.Reflection;
using Grpc.Core;
using Grpc.Net.Client;
using System.Runtime.CompilerServices;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace GrpCurl.Net.Invocation;

/// <summary>
///     Handles dynamic invocation of gRPC methods without pre-compiled stubs.
/// </summary>
internal sealed class DynamicInvoker(GrpcChannel channel)
{
    private static readonly JsonSerializerOptions IndentedJsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private static readonly JsonSerializerOptions CompactJsonOptions = new()
    {
        WriteIndented = false,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    /// <summary>
    ///     How long teardown waits for a cancelled bidi request producer to unwind, so a fault
    ///     raised by the caller's own request source is still surfaced. Bounded on purpose: a
    ///     source can be parked in an operation that ignores cancellation — an interactive stdin
    ///     read is the canonical case — and blocking on that is the hang PRD-003 fixes.
    /// </summary>
    internal static readonly TimeSpan WriterDrainGrace = TimeSpan.FromMilliseconds(250);

    private readonly GrpcChannel _channel = channel ?? throw new ArgumentNullException(nameof(channel));

    /// <summary>
    ///     Invokes a unary RPC method.
    /// </summary>
    public async Task<InvocationResult> InvokeUnaryAsync(
        MethodDescriptor methodDescriptor,
        IMessage request,
        Metadata? headers = null,
        DateTime? deadline = null,
        CancellationToken cancellationToken = default)
    {
        // Check cancellation before starting the call to throw OperationCanceledException
        // instead of RpcException for pre-canceled tokens (more idiomatic .NET behavior)
        cancellationToken.ThrowIfCancellationRequested();

        var method = CreateMethod<IMessage, IMessage>(methodDescriptor, MethodType.Unary);
        var callInvoker = _channel.CreateCallInvoker();

        var callOptions = new CallOptions(headers, deadline, cancellationToken);

        var call = callInvoker.AsyncUnaryCall(
            method,
            null,
            callOptions,
            request);

        IMessage response;

        try
        {
            response = await call.ResponseAsync;
        }
        catch (RpcException ex)
        {
            throw await AttachResponseHeadersAsync(
                RpcErrorNormalizer.Normalize(ex, deadline, cancellationToken.IsCancellationRequested), call.ResponseHeadersAsync);
        }

        var responseHeaders = await call.ResponseHeadersAsync;

        Metadata? responseTrailers = null;

        try
        {
            responseTrailers = call.GetTrailers();
        }
        catch
        {
            // Trailers may not be available
        }

        return new InvocationResult
        {
            Response = response,
            ResponseHeaders = responseHeaders,
            ResponseTrailers = responseTrailers
        };
    }

    /// <summary>
    ///     Invokes a server-streaming RPC method.
    /// </summary>
    public async IAsyncEnumerable<IMessage> InvokeServerStreamingAsync(
        MethodDescriptor methodDescriptor,
        IMessage request,
        Metadata? headers = null,
        DateTime? deadline = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        // Check cancellation before starting the call
        cancellationToken.ThrowIfCancellationRequested();

        var method = CreateMethod<IMessage, IMessage>(methodDescriptor, MethodType.ServerStreaming);
        var callInvoker = _channel.CreateCallInvoker();
        var callOptions = new CallOptions(headers, deadline, cancellationToken);
        var call = callInvoker.AsyncServerStreamingCall(
            method,
            null,
            callOptions,
            request);

        await foreach (var response in RemapDeadlineExpiry(call.ResponseStream.ReadAllAsync(cancellationToken), deadline, cancellationToken))
        {
            yield return response;
        }
    }

    /// <summary>
    ///     Server-streaming variant that surfaces response headers and trailers alongside
    ///     the stream so verbose CLI output can render them uniformly with the unary path.
    ///     Caller must <see cref="StreamingInvocationResult.DisposeAsync" /> when finished.
    /// </summary>
    public StreamingInvocationResult InvokeServerStreamingWithMetadataAsync(
        MethodDescriptor methodDescriptor,
        IMessage request,
        Metadata? headers = null,
        DateTime? deadline = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var method = CreateMethod<IMessage, IMessage>(methodDescriptor, MethodType.ServerStreaming);
        var callInvoker = _channel.CreateCallInvoker();
        var callOptions = new CallOptions(headers, deadline, cancellationToken);
        var call = callInvoker.AsyncServerStreamingCall(method, null, callOptions, request);

        return new StreamingInvocationResult(
            call.ResponseHeadersAsync,
            RemapDeadlineExpiry(call.ResponseStream.ReadAllAsync(cancellationToken), deadline, cancellationToken),
            call.GetTrailers,
            call.Dispose);
    }

    /// <summary>
    ///     Invokes a client-streaming RPC method.
    /// </summary>
    public async Task<IMessage> InvokeClientStreamingAsync(
        MethodDescriptor methodDescriptor,
        IAsyncEnumerable<IMessage> requests,
        Metadata? headers = null,
        DateTime? deadline = null,
        CancellationToken cancellationToken = default)
    {
        var result = await InvokeClientStreamingWithMetadataAsync(methodDescriptor, requests, headers, deadline, cancellationToken).ConfigureAwait(false);

        try
        {
            return result.Response;
        }
        finally
        {
            result.Dispose();
        }
    }

    /// <summary>
    ///     Client-streaming variant that returns the response along with headers and trailers
    ///     so verbose CLI output can render them uniformly with the unary path.
    /// </summary>
    public async Task<ClientStreamingInvocationResult> InvokeClientStreamingWithMetadataAsync(
        MethodDescriptor methodDescriptor,
        IAsyncEnumerable<IMessage> requests,
        Metadata? headers = null,
        DateTime? deadline = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var method = CreateMethod<IMessage, IMessage>(methodDescriptor, MethodType.ClientStreaming);
        var callInvoker = _channel.CreateCallInvoker();
        var callOptions = new CallOptions(headers, deadline, cancellationToken);
        var call = callInvoker.AsyncClientStreamingCall(method, null, callOptions);

        try
        {
            await foreach (var request in requests.WithCancellation(cancellationToken))
            {
                await call.RequestStream.WriteAsync(request, cancellationToken);
            }

            await call.RequestStream.CompleteAsync();

            var response = await call.ResponseAsync;

            return new ClientStreamingInvocationResult(
                call.ResponseHeadersAsync,
                response,
                call.GetTrailers,
                call.Dispose);
        }
        catch (RpcException ex)
        {
            throw await AttachResponseHeadersAsync(
                RpcErrorNormalizer.Normalize(ex, deadline, cancellationToken.IsCancellationRequested), call.ResponseHeadersAsync);
        }
    }

    /// <summary>
    ///     Captures the response headers (when the server delivered any before failing)
    ///     so error paths can surface them via <see cref="RpcInvocationException" />.
    ///     Connection-level failures, where the headers task faults, rethrow unchanged.
    /// </summary>
    private static async Task<RpcException> AttachResponseHeadersAsync(RpcException exception, Task<Metadata> responseHeadersTask)
    {
        Metadata responseHeaders;

        try
        {
            responseHeaders = await responseHeadersTask.ConfigureAwait(false);
        }
        catch
        {
            return exception;
        }

        return new RpcInvocationException(exception, responseHeaders);
    }

    /// <summary>
    ///     Applies <see cref="RpcErrorNormalizer.Normalize" /> to failures observed while
    ///     enumerating a response stream.
    /// </summary>
    private static async IAsyncEnumerable<IMessage> RemapDeadlineExpiry(
        IAsyncEnumerable<IMessage> source,
        DateTime? deadline,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await using var enumerator = source.GetAsyncEnumerator(cancellationToken);

        while (true)
        {
            IMessage current;

            try
            {
                if (!await enumerator.MoveNextAsync().ConfigureAwait(false))
                {
                    break;
                }

                current = enumerator.Current;
            }
            catch (RpcException ex)
            {
                throw RpcErrorNormalizer.Normalize(ex, deadline, cancellationToken.IsCancellationRequested);
            }

            yield return current;
        }
    }

    /// <summary>
    ///     Bidi-streaming variant exposing response headers and trailers alongside the
    ///     downstream message enumerable. Ownership of the request producer — its linked
    ///     cancellation source and its task — passes to the returned result, which the caller
    ///     must dispose with <c>await using</c>.
    /// </summary>
    public StreamingInvocationResult InvokeDuplexStreamingWithMetadataAsync(
        MethodDescriptor methodDescriptor,
        IAsyncEnumerable<IMessage> requests,
        Metadata? headers = null,
        DateTime? deadline = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var method = CreateMethod<IMessage, IMessage>(methodDescriptor, MethodType.DuplexStreaming);
        var callInvoker = _channel.CreateCallInvoker();
        var callOptions = new CallOptions(headers, deadline, cancellationToken);
        var call = callInvoker.AsyncDuplexStreamingCall(method, null, callOptions);

        CancellationTokenSource? writerCts = null;

        try
        {
            // Linked so the response side can stop the producer without touching the caller's
            // token. Deliberately not `using`-scoped: the returned result owns and disposes it.
            writerCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

            var writerToken = writerCts.Token;

            // Capture the stream so the producer does not close over 'call', which is disposed
            // by the result while the producer may still hold the request stream.
            var requestStream = call.RequestStream;

            // CancellationToken.None: a token already cancelled when the task is scheduled must
            // still run the pump's own unwind rather than leave the task Canceled and unshaped.
            var writeTask = Task.Run(() => PumpRequestsAsync(requests, requestStream, writerToken), CancellationToken.None);

            return new StreamingInvocationResult(
                call.ResponseHeadersAsync,
                ReadAll(writerCts, writeTask),
                call.GetTrailers,
                call.Dispose,
                writerCts,
                writeTask);
        }
        catch
        {
            // Nothing took ownership of the call, so release it here rather than leaking the
            // HTTP/2 stream.
            writerCts?.Dispose();
            call.Dispose();

            throw;
        }

        async IAsyncEnumerable<IMessage> ReadAll(CancellationTokenSource producerCts, Task producerTask)
        {
            try
            {
                // INVARIANT: the CALLER's token is passed here, never the writer's. It is what
                // RpcErrorNormalizer.NormalizeClientCancellation keys on, so an internal
                // producer cancel must never be able to masquerade as caller cancellation.
                await foreach (var response in RemapDeadlineExpiry(call.ResponseStream.ReadAllAsync(cancellationToken), deadline, cancellationToken).ConfigureAwait(false))
                {
                    yield return response;
                }
            }
            finally
            {
                // The response side is finished — cleanly, with an error, or because the
                // consumer abandoned enumeration. No further write can reach the server, so
                // stop the producer now instead of leaving it parked on its source.
                await StreamingInvocationResult.CancelQuietlyAsync(producerCts).ConfigureAwait(false);
            }

            // Reached only on clean completion: the server returned OK, so the RPC succeeded and
            // the read side has nothing left to report. The one thing still worth surfacing is a
            // fault raised by the caller's own request source — PumpRequestsAsync guarantees that
            // is all this task's fault channel can carry.
            // INVARIANT: never await the producer unbounded (see WriterDrainGrace).
            try
            {
                // CancellationToken.None: the grace is already bounded, and the caller's token
                // has usually just fired on the cancellation paths that reach here.
                await producerTask.WaitAsync(WriterDrainGrace, CancellationToken.None).ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
                // Producer parked in an operation that ignores cancellation. The RPC already
                // succeeded; observe any later fault rather than hanging the consumer on it.
                ObserveFault(producerTask);
            }
            catch (OperationCanceledException)
            {
                // Producer unwound on cancellation. Not an RPC failure.
            }
            catch (RpcException ex)
            {
                // A write that failed on its own merits (an oversize message, a marshaller
                // failure) rather than because the call had already finished.
                throw RpcErrorNormalizer.Normalize(ex, deadline, cancellationToken.IsCancellationRequested);
            }

            // Anything else is an error from the caller's request source — malformed JSON, a
            // stdin limit breach — and propagates unchanged so the CLI still reports it.
        }
    }

    /// <summary>
    ///     Drives the caller's request source into the call's request stream.
    ///     <para>
    ///         Transport failures are absorbed at the exact await that produced them, so this
    ///         task's fault channel carries ONLY exceptions raised by the caller's source. That
    ///         is what lets the read side tell "your request JSON was malformed" apart from "the
    ///         server closed its half of the stream while a write was in flight" without having
    ///         to guess from exception types, which are ambiguous between the two.
    ///     </para>
    /// </summary>
    private static async Task PumpRequestsAsync(
        IAsyncEnumerable<IMessage> requests,
        IClientStreamWriter<IMessage> requestStream,
        CancellationToken writerToken)
    {
        await using var enumerator = requests.GetAsyncEnumerator(writerToken);

        while (true)
        {
            IMessage message;

            try
            {
                if (!await enumerator.MoveNextAsync().ConfigureAwait(false))
                {
                    break;
                }

                message = enumerator.Current;
            }
            catch (OperationCanceledException) when (writerToken.IsCancellationRequested)
            {
                // The response side finished, the consumer walked away, or the caller cancelled:
                // the source was torn down deliberately. Deliberately no CompleteAsync — once
                // writes are moot, half-closing only perturbs the wire shape the conformance
                // suite's before_close_send cases observe.
                return;
            }

            try
            {
                await requestStream.WriteAsync(message, writerToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (IsPostCompletionWriteNoise(ex, writerToken))
            {
                return;
            }
        }

        try
        {
            await requestStream.CompleteAsync().ConfigureAwait(false);
        }
        catch (Exception ex) when (IsPostCompletionWriteNoise(ex, writerToken))
        {
            // The half-close raced the server's own completion; immaterial for the same reason.
        }
    }

    /// <summary>
    ///     True when a write-half failure is a consequence of the call already being finished
    ///     rather than a fault of its own. A gRPC call's status is the server's status, delivered
    ///     on the read half; a write that could not land is not an RPC failure (upstream grpcurl
    ///     likewise discards <c>SendMsg</c>'s <c>io.EOF</c> and reports the status from
    ///     <c>RecvMsg</c>).
    ///     <para>
    ///         Shapes, verified against Grpc.Net.Client 2.76.0: once the call has completed,
    ///         <c>HttpContentClientStreamWriter.WriteAsync</c> hands back
    ///         <c>GrpcCall.CreateCanceledStatusException()</c>, which carries the completed call's
    ///         own status — so a server that finished cleanly yields an <see cref="RpcException" />
    ///         whose status code is <see cref="StatusCode.OK" />. Half-close after completion gives
    ///         <see cref="InvalidOperationException" />, writing to a disposed call gives
    ///         <see cref="ObjectDisposedException" />, and a torn-down HTTP/2 stream gives
    ///         <see cref="IOException" />.
    ///     </para>
    ///     <para>
    ///         The <see cref="RpcException" /> arm is filtered rather than blanket-swallowed so a
    ///         write that genuinely failed on its own merits — an oversize message giving
    ///         RESOURCE_EXHAUSTED, a marshaller failure — still faults the pump and reaches the
    ///         caller.
    ///     </para>
    /// </summary>
    private static bool IsPostCompletionWriteNoise(Exception exception, CancellationToken writerToken) => exception switch
    {
        OperationCanceledException => true,
        // ObjectDisposedException derives from InvalidOperationException, so this one arm covers
        // both the half-close-after-completion and the write-to-a-disposed-call shapes.
        InvalidOperationException => true,
        IOException => true,
        RpcException rpc => writerToken.IsCancellationRequested || rpc.StatusCode == StatusCode.OK,
        _ => false
    };

    /// <summary>
    ///     Consumes a task's eventual fault so an abandoned producer cannot surface it as an
    ///     unobserved task exception.
    /// </summary>
    internal static void ObserveFault(Task task)
        => _ = task.ContinueWith(
            static completed => _ = completed.Exception,
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);

    /// <summary>
    ///     Invokes a bidirectional-streaming RPC method.
    /// </summary>
    public async IAsyncEnumerable<IMessage> InvokeDuplexStreamingAsync(
        MethodDescriptor methodDescriptor,
        IAsyncEnumerable<IMessage> requests,
        Metadata? headers = null,
        DateTime? deadline = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        // Check cancellation before starting the call
        cancellationToken.ThrowIfCancellationRequested();

        var method = CreateMethod<IMessage, IMessage>(methodDescriptor, MethodType.DuplexStreaming);
        var callInvoker = _channel.CreateCallInvoker();
        var callOptions = new CallOptions(headers, deadline, cancellationToken);
        var call = callInvoker.AsyncDuplexStreamingCall(
            method,
            null,
            callOptions);

        // Create a linked token source for the write task so we can cancel it independently
        using var writeCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        var writeToken = writeCts.Token;

        Exception? readException = null;

        // Capture the streams so the write task lambda does not capture 'call' itself.
        // This avoids an AccessToDisposedClosure issue: 'call' is disposed in the finally
        // block below, and the write task must not hold a reference to it.
        var requestStream = call.RequestStream;

        // Start writing requests in background
        var writeTask = Task.Run(async () =>
        {
            var sentCount = 0;

            try
            {
                await foreach (var request in requests.WithCancellation(writeToken))
                {
                    await requestStream.WriteAsync(request, writeToken);
                    sentCount++;
                }

                await requestStream.CompleteAsync();
            }
            catch (OperationCanceledException) when (writeToken.IsCancellationRequested)
            {
                // Write was cancelled - this is expected if response stream ended early or user cancelled
                // Complete the stream if we can, ignore errors
                try
                {
                    await requestStream.CompleteAsync();
                }
                catch
                {
                    // Ignore - stream may already be closed
                }
            }
            catch (RpcException ex)
            {
                // Mid-stream RPC error - provide context about partial results
                throw new RpcException(new Status(ex.StatusCode, $"Error after sending {sentCount} message(s): {ex.Status.Detail}"), ex.Trailers);
            }
            catch (IOException ex)
            {
                // Connection drop during write
                throw new IOException($"Connection lost after sending {sentCount} message(s)", ex);
            }
        }, writeToken);

        // Read responses
        try
        {
            await foreach (var response in RemapDeadlineExpiry(call.ResponseStream.ReadAllAsync(cancellationToken), deadline, cancellationToken))
            {
                yield return response;
            }
        }
        finally
        {
            // Cancel the write task if it's still running
            await writeCts.CancelAsync();

            // Await the write task to completion before disposing the call.
            // The cancellation above ensures the task terminates promptly.
            // We must NOT dispose 'call' while the write task is still running,
            // as the task holds a reference to the request stream.
            try
            {
                await writeTask;
            }
            catch (OperationCanceledException)
            {
                // Expected - write was cancelled
            }
            catch (Exception ex)
            {
                // Write task failed - propagate if no read exception occurred
                readException ??= ex;
            }

            // Safe to dispose now - write task has fully completed
            call.Dispose();
        }

        // Propagate any write exception that occurred
        if (readException is not null)
        {
            throw readException;
        }
    }

    private static Method<TRequest, TResponse> CreateMethod<TRequest, TResponse>(
        MethodDescriptor methodDescriptor,
        MethodType methodType)
        where TRequest : class
        where TResponse : class
        => new(methodType,
               methodDescriptor.Service.FullName,
               methodDescriptor.Name,
               CreateMarshaller<TRequest>(methodDescriptor.InputType),
               CreateMarshaller<TResponse>(methodDescriptor.OutputType));

    private static Marshaller<T> CreateMarshaller<T>(MessageDescriptor messageDescriptor)
        where T : class
    {
        return new Marshaller<T>(
            message =>
            {
                if (message is IMessage protoMessage)
                {
                    return protoMessage.ToByteArray();
                }

                throw new ArgumentException($"Message must be an IMessage, got {message.GetType()}");
            },
            bytes =>
            {
                // Create a SimpleDynamicMessage and parse the bytes
                var dynamicMessage = new SimpleDynamicMessage(messageDescriptor);

                try
                {
                    using var input = new CodedInputStream(bytes);

                    dynamicMessage.MergeFrom(input);
                }
                catch (InvalidProtocolBufferException ex)
                {
                    // A malformed response message is INTERNAL per the gRPC spec; a raw
                    // protobuf exception escaping the marshaller would surface as
                    // UNAVAILABLE ("Error starting gRPC call") instead.
                    throw new RpcException(new Status(StatusCode.Internal, $"Failed to deserialize response message: {ex.Message}", ex));
                }

                return (T)(object)dynamicMessage;
            });
    }

    /// <summary>
    ///     Creates a request message from JSON input.
    /// </summary>
    public static IMessage CreateMessageFromJson(MessageDescriptor messageDescriptor, string? json, bool allowUnknownFields = true) =>
        // .NET Google.Protobuf doesn't natively support dynamic message creation
        // As a workaround, we'll create a simple dynamic message implementation
        new SimpleDynamicMessage(messageDescriptor, json, allowUnknownFields);

    /// <summary>
    ///     Converts a message to JSON. Defaults to pretty-printed (matching Go grpcurl);
    ///     pass <paramref name="indent" /> = <c>false</c> for one-line compact output
    ///     suitable for NDJSON streaming.
    /// </summary>
    public static string MessageToJson(IMessage message, bool includeDefaults = false, bool indent = true)
    {
        string compactJson;

        // Handle SimpleDynamicMessage specially
        if (message is SimpleDynamicMessage dynamicMessage)
        {
            compactJson = dynamicMessage.ToJson(includeDefaults);
        }
        else
        {
            // For regular messages, use built-in formatter
            var formatter = new JsonFormatter(new JsonFormatter.Settings(includeDefaults));

            compactJson = formatter.Format(message);
        }

        using var doc = JsonDocument.Parse(compactJson);

        return JsonSerializer.Serialize(doc.RootElement, indent ? IndentedJsonOptions : CompactJsonOptions);
    }
}