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

        var response = await call.ResponseAsync;
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

        await foreach (var response in call.ResponseStream.ReadAllAsync(cancellationToken))
        {
            yield return response;
        }
    }

    /// <summary>
    ///     Server-streaming variant that surfaces response headers and trailers alongside
    ///     the stream so verbose CLI output can render them uniformly with the unary path.
    ///     Caller must <see cref="StreamingInvocationResult.Dispose" /> when finished.
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
            call.ResponseStream.ReadAllAsync(cancellationToken),
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

    /// <summary>
    ///     Bidi-streaming variant exposing response headers and trailers alongside the
    ///     downstream message enumerable. Internally manages the write task identically
    ///     to <see cref="InvokeDuplexStreamingAsync" />; caller must dispose the result.
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

        var requestStream = call.RequestStream;

        var writeTask = Task.Run(async () =>
        {
            try
            {
                await foreach (var msg in requests.WithCancellation(cancellationToken).ConfigureAwait(false))
                {
                    await requestStream.WriteAsync(msg, cancellationToken).ConfigureAwait(false);
                }

                await requestStream.CompleteAsync().ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Caller cancelled — let the read side see the cancellation too.
            }
        }, cancellationToken);

        return new StreamingInvocationResult(
            call.ResponseHeadersAsync,
            ReadAll(),
            call.GetTrailers,
            call.Dispose);

        async IAsyncEnumerable<IMessage> ReadAll()
        {
            await foreach (var response in call.ResponseStream.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            {
                yield return response;
            }

            await writeTask.ConfigureAwait(false);
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
            await foreach (var response in call.ResponseStream.ReadAllAsync(cancellationToken))
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

                using (var input = new CodedInputStream(bytes))
                {
                    dynamicMessage.MergeFrom(input);
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