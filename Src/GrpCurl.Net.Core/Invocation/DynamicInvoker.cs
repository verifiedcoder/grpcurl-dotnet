using Google.Protobuf;
using Google.Protobuf.Reflection;
using Grpc.Core;
using Grpc.Net.Client;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
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

        // Nothing outlives this method: InvocationResult holds materialized values, never the call.
        // Every exit here is already terminal — a response arrived, or a status did — so unlike the
        // streaming paths this releases managed call state rather than a live HTTP/2 stream, but it
        // is the same ownership rule and the same reason to state it once (PRD-004).
        try
        {
            IMessage response;

            try
            {
                response = await call.ResponseAsync;
            }
            catch (RpcException ex)
            {
                // Read inside the try, so the call is still live when the headers are taken. By here
                // the call is terminal, so the header task has already resolved and the
                // never-wait rule in AttachResponseHeaders costs this path nothing.
                throw AttachResponseHeaders(
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
        finally
        {
            call.Dispose();
        }
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

        // Unlike the metadata overload there is no result object to hand ownership to, so this
        // iterator owns the call for its whole lifetime. The finally runs when the consumer's
        // enumerator is disposed — including on `break` and on a fault — which is the only thing
        // that releases an in-flight call whose server is still streaming. Gql2Grpc's subscription
        // path (Execution/GrpcTransport.cs) is the production consumer that can break mid-stream.
        try
        {
            await foreach (var response in RemapDeadlineExpiry(call.ResponseStream.ReadAllAsync(cancellationToken), deadline, cancellationToken))
            {
                yield return response;
            }
        }
        finally
        {
            call.Dispose();
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

        AsyncServerStreamingCall<IMessage>? call = null;

        // Defensive only, and deliberately so: there is no await between creating the call and
        // handing it to the result, so nothing in this window can fail today. It states the same
        // ownership rule the duplex overload below enforces for real, so a future edit that adds
        // work here cannot silently reintroduce a leak.
        try
        {
            call = callInvoker.AsyncServerStreamingCall(method, null, callOptions, request);

            return new StreamingInvocationResult(
                call.ResponseHeadersAsync,
                RemapDeadlineExpiry(call.ResponseStream.ReadAllAsync(cancellationToken), deadline, cancellationToken),
                call.GetTrailers,
                call.Dispose);
        }
        catch
        {
            call?.Dispose();

            throw;
        }
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

        // Ownership moves to the result only if we get as far as constructing one. Every other exit —
        // a fault from the caller's source, a write that failed on its own merits, a server status,
        // caller cancellation — leaves us holding the call, and an undisposed AsyncClientStreamingCall
        // keeps its HTTP/2 stream open. On the paths where we never half-closed, that also strands the
        // server in ReadAllAsync waiting on a request stream nobody will finish (PRD-004).
        var ownershipTransferred = false;

        try
        {
            await foreach (var request in requests.WithCancellation(cancellationToken))
            {
                await call.RequestStream.WriteAsync(request, cancellationToken);
            }

            await call.RequestStream.CompleteAsync();

            var response = await call.ResponseAsync;

            var invocation = new ClientStreamingInvocationResult(
                call.ResponseHeadersAsync,
                response,
                call.GetTrailers,
                call.Dispose);

            // Assigned before the return rather than inside it: `finally` runs after the returned
            // value has been computed, so returning the expression directly would put the flag
            // beyond the finally's reach.
            //
            // No test can catch its removal, and that is stated rather than papered over: by here
            // `await call.ResponseAsync` has returned, so the call is terminal and the double
            // disposal that dropping the flag would cause is invisible. The flag is what keeps that
            // from being load-bearing — correctness should not rest on grpc-dotnet's Dispose
            // happening to be idempotent, nor on the success path never gaining work before the
            // handover.
            ownershipTransferred = true;

            return invocation;
        }
        catch (RpcException ex)
        {
            // INVARIANT: this catch and the finally below belong to the SAME try, so the response
            // headers are taken while the call is still live. Splitting them — an outer catch
            // wrapping an inner try/finally — would dispose first, cancel the headers task, and
            // silently downgrade RpcInvocationException to a bare RpcException on every error path.
            //
            // The second half of that invariant is that nothing here may WAIT. This catch runs before
            // the finally that releases the call, so a wait on a header task the server has not
            // resolved postpones the release for as long as the server likes — which is how a
            // pre-header write failure stayed live despite the finally (PRD-004 review, finding 1).
            // AttachResponseHeaders therefore takes what has already arrived and never blocks.
            throw AttachResponseHeaders(
                RpcErrorNormalizer.Normalize(ex, deadline, cancellationToken.IsCancellationRequested), call.ResponseHeadersAsync);
        }
        finally
        {
            if (!ownershipTransferred)
            {
                // Disposing an in-flight call resets the HTTP/2 stream, which is also what releases
                // a server still reading the request stream we abandoned.
                call.Dispose();
            }
        }
    }

    /// <summary>
    ///     Captures the response headers (when the server delivered any before failing)
    ///     so error paths can surface them via <see cref="RpcInvocationException" />.
    ///     Connection-level failures, where the headers task faults, rethrow unchanged.
    /// </summary>
    /// <remarks>
    ///     Never waits on a header task that has not already resolved. Enrichment is diagnostic, but
    ///     the wait is not free: this runs inside a <c>catch</c> whose <c>finally</c> releases the
    ///     call, so blocking here holds the very resource the caller is trying to let go of. A write
    ///     that fails before the server sends anything — an oversize message against a server still
    ///     reading — leaves the header task pending for as long as that server chooses, which is
    ///     unbounded (PRD-004 review, finding 1).
    ///     <para>
    ///         The cost is that such a failure surfaces as a bare <see cref="RpcException" /> rather
    ///         than an <see cref="RpcInvocationException" />. That is the intended trade: there were
    ///         no headers to report at the moment it failed, and prompt release is worth more than
    ///         metadata that may never arrive. Paths where the server did send headers before failing
    ///         — every server-reported status — resolve the task first and still enrich.
    ///     </para>
    /// </remarks>
    private static RpcException AttachResponseHeaders(RpcException exception, Task<Metadata> responseHeadersTask)
    {
        if (!responseHeadersTask.IsCompletedSuccessfully)
        {
            // Pending, faulted or cancelled — all three mean there is nothing to attach right now.
            // Observe it either way: releasing the call faults this task, and nobody is left waiting
            // on it, so an unobserved exception would surface at finalization instead.
            ObserveInBackground(responseHeadersTask);

            return exception;
        }

        return new RpcInvocationException(exception, responseHeadersTask.Result);
    }

    /// <summary>
    ///     Swallows the eventual outcome of a task nobody awaits, so a later fault cannot escape as an
    ///     unobserved task exception.
    /// </summary>
    private static void ObserveInBackground(Task task) =>
        _ = task.ContinueWith(
            static observed => _ = observed.Exception,
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);

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

        // The call runs on a linked token rather than the caller's own so a failed request producer
        // can abort it, which is the only bounded way to release a reader whose server has not
        // finished. Caller cancellation still reaches the call through the link, and normalization
        // below continues to key off the caller's token so the two can never be confused.
        var callCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        AsyncDuplexStreamingCall<IMessage, IMessage>? call = null;

        try
        {
            var callOptions = new CallOptions(headers, deadline, callCts.Token);

            call = callInvoker.AsyncDuplexStreamingCall(method, null, callOptions);

            // The producer takes ownership of callCts, and holds the request stream rather than the
            // call, which the result disposes while the producer may still be running.
            var producer = DuplexRequestProducer.Start(requests, call.RequestStream, callCts);

            return new StreamingInvocationResult(
                call.ResponseHeadersAsync,
                ReadAll(producer),
                call.GetTrailers,
                call.Dispose,
                producer);
        }
        catch
        {
            // Nothing took ownership, so release here rather than leaking the HTTP/2 stream.
            call?.Dispose();
            callCts.Dispose();

            throw;
        }

        async IAsyncEnumerable<IMessage> ReadAll(DuplexRequestProducer producer)
        {
            try
            {
                // INVARIANT: the CALLER's token is passed here, never the call's or the writer's. It
                // is what RpcErrorNormalizer.NormalizeClientCancellation keys on, so an internal
                // abort must never be able to masquerade as caller cancellation.
                var responses = RemapDeadlineExpiry(call.ResponseStream.ReadAllAsync(cancellationToken), deadline, cancellationToken);

                await using var enumerator = responses.GetAsyncEnumerator(cancellationToken);

                while (true)
                {
                    IMessage response;

                    try
                    {
                        if (!await enumerator.MoveNextAsync().ConfigureAwait(false))
                        {
                            break;
                        }

                        response = enumerator.Current;
                    }
                    catch (Exception readFault)
                    {
                        // Read the producer's fault BEFORE stopping it, so only one that was already
                        // recorded — and therefore preceded, and plausibly caused, this status — can
                        // win. A fault our own cancellation goes on to create must never displace the
                        // error the server actually reported. (The producer declines to record such
                        // faults at all; this ordering is the second half of the same guarantee.)
                        //
                        // A SOURCE fault always displaces it: the read half cannot know the caller's
                        // own enumerable failed. A WRITE fault normally must not, because a write-side
                        // failure is otherwise just a shadow of the call failing and the server's
                        // status is the better report — except when the read failure is the artifact
                        // this producer's own abort manufactured to release us, in which case
                        // discarding the write fault would lose the only real error there is.
                        //
                        // Both artifact shapes count: cancelling a grpc-dotnet call surfaces either
                        // CANCELLED or, when HTTP/2 teardown beats token propagation, the
                        // aborted-request UNAVAILABLE. RpcErrorNormalizer owns that predicate — it
                        // cannot be recognised here by status alone, and the caller's token is
                        // deliberately not the one we cancelled, so Normalize leaves the second shape
                        // as UNAVAILABLE.
                        var causalFault = producer.Fault switch
                        {
                            { FromWrite: false } source => source.Exception,
                            { FromWrite: true } write when producer.AbortedCall
                                                           && readFault is RpcException rpcFault
                                                           && RpcErrorNormalizer.IsCancellationArtifact(rpcFault)
                                => write.Exception,
                            _ => null
                        };

                        ExceptionDispatchInfo.Capture(causalFault ?? readFault).Throw();

                        throw;
                    }

                    yield return response;
                }
            }
            finally
            {
                // The response side is finished — cleanly, with an error, or because the consumer
                // abandoned enumeration. Tell the producer to stop waiting to abort a call that is
                // already over, then stop it: no further write can reach the server.
                producer.OnResponseEnded();

                await producer.CancelAsync().ConfigureAwait(false);
            }

            // Reached only on clean completion: the server returned OK, so the RPC succeeded and the
            // read side has nothing left to report. The one thing still worth surfacing is a fault
            // the producer raised of its own accord — never one this drain's cancellation provokes,
            // which the producer declines to record, so an OK call stays OK.
            // INVARIANT: never await the producer unbounded (see WriterDrainGrace).
            var lateFault = await producer.DrainAsync(WriterDrainGrace).ConfigureAwait(false);

            switch (lateFault)
            {
                case null:
                    break;

                case { FromWrite: true, Exception: RpcException rpc }:
                    // A write that failed on its own merits (an oversize message, a marshaller
                    // failure) belongs to this call, so it is normalized exactly like a read fault.
                    throw RpcErrorNormalizer.Normalize(rpc, deadline, cancellationToken.IsCancellationRequested);

                default:
                    // The caller's own error — malformed JSON, a stdin limit breach, or an
                    // RpcException belonging to a gRPC-backed source's own call — propagates
                    // untouched, exactly as it does on the read-error path.
                    ExceptionDispatchInfo.Capture(lateFault.Exception).Throw();

                    break;
            }
        }
    }

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