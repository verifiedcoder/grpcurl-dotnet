using Google.Protobuf;
using Grpc.Core;
using GrpCurl.Net.TestServer.Protos;

namespace GrpCurl.Net.TestServer.Services;

/// <summary>
///     Implementation of TestService with metadata-driven behavior control.
///     Mirrors the Go grpcurl test server for feature parity testing.
/// </summary>
public class TestServiceImpl : TestService.TestServiceBase
{
    /// <summary>
    ///     One empty request followed by one empty response.
    /// </summary>
    public override async Task<Empty> EmptyCall(Empty request, ServerCallContext context)
    {
        var (headers, trailers, failEarly, failLate, delayMs) = MetadataProcessor.ProcessMetadata(context);

        await MetadataProcessor.SetResponseHeadersAsync(context, headers);

        MetadataProcessor.SetResponseTrailers(context, trailers);

        if (failEarly.HasValue)
        {
            throw new RpcException(new Status(failEarly.Value, "fail"));
        }

        if (delayMs > 0)
        {
            await Task.Delay(delayMs, context.CancellationToken);
        }

        return failLate.HasValue
            ? throw new RpcException(new Status(failLate.Value, "fail"))
            : request;
    }

    /// <summary>
    ///     One request followed by one response. Honours the four interop fields called out
    ///     in CODE-REVIEW.md P1 "Test server doesn't model interop behaviour":
    ///     <list type="bullet">
    ///       <item><c>response_size</c> — fills the payload to the requested byte size.</item>
    ///       <item><c>fill_username</c> — populates <c>SimpleResponse.username</c> from the
    ///         <c>x-test-username</c> request header.</item>
    ///       <item><c>fill_oauth_scope</c> — populates <c>SimpleResponse.oauth_scope</c> from
    ///         the <c>x-test-oauth-scope</c> request header.</item>
    ///       <item><c>response_status</c> — when its code is non-zero, fails the RPC with
    ///         that status and the supplied message.</item>
    ///     </list>
    /// </summary>
    public override async Task<SimpleResponse> UnaryCall(SimpleRequest request, ServerCallContext context)
    {
        var (headers, trailers, failEarly, failLate, delayMs) = MetadataProcessor.ProcessMetadata(context);

        await MetadataProcessor.SetResponseHeadersAsync(context, headers);

        MetadataProcessor.SetResponseTrailers(context, trailers);

        if (failEarly.HasValue)
        {
            throw new RpcException(new Status(failEarly.Value, "fail"));
        }

        if (delayMs > 0)
        {
            await Task.Delay(delayMs, context.CancellationToken);
        }

        if (failLate.HasValue)
        {
            throw new RpcException(new Status(failLate.Value, "fail"));
        }

        if (request.ResponseStatus is { Code: not 0 } responseStatus)
        {
            throw new RpcException(new Status((StatusCode)responseStatus.Code, responseStatus.Message ?? string.Empty));
        }

        var responsePayload = BuildPayload(request.Payload, request.ResponseType, request.ResponseSize);

        var response = new SimpleResponse
        {
            Payload = responsePayload
        };

        if (request.FillUsername)
        {
            response.Username = GetHeaderValue(context, "x-test-username") ?? "anonymous";
        }

        if (request.FillOauthScope)
        {
            response.OauthScope = GetHeaderValue(context, "x-test-oauth-scope") ?? string.Empty;
        }

        return response;
    }

    private static Payload BuildPayload(Payload? existingPayload, PayloadType responseType, int responseSize)
    {
        if (responseSize <= 0)
        {
            return existingPayload ?? new Payload { Type = responseType };
        }

        var body = new byte[responseSize];

        for (var i = 0; i < responseSize; i++)
        {
            body[i] = (byte)(i % 256);
        }

        return new Payload
        {
            Type = responseType,
            Body = ByteString.CopyFrom(body)
        };
    }

    private static string? GetHeaderValue(ServerCallContext context, string headerName)
        => (from entry in context.RequestHeaders
            where string.Equals(entry.Key, headerName, StringComparison.OrdinalIgnoreCase) && !entry.IsBinary
            select entry.Value).FirstOrDefault();

    /// <summary>
    ///     One request followed by a sequence of responses (streamed download).
    ///     The server returns the payload with client desired type and sizes.
    /// </summary>
    public override async Task StreamingOutputCall(
        StreamingOutputCallRequest request,
        IServerStreamWriter<StreamingOutputCallResponse> responseStream,
        ServerCallContext context)
    {
        var (headers, trailers, failEarly, failLate, delayMs) = MetadataProcessor.ProcessMetadata(context);

        await MetadataProcessor.SetResponseHeadersAsync(context, headers);

        MetadataProcessor.SetResponseTrailers(context, trailers);

        if (failEarly.HasValue)
        {
            throw new RpcException(new Status(failEarly.Value, "fail"));
        }

        foreach (var param in request.ResponseParameters)
        {
            context.CancellationToken.ThrowIfCancellationRequested();

            // Add delay between responses if specified
            var intervalUs = param.IntervalUs;

            if (intervalUs > 0)
            {
                await Task.Delay(TimeSpan.FromMicroseconds(intervalUs), context.CancellationToken);
            }

            // Also honor the delay-ms header
            if (delayMs > 0)
            {
                await Task.Delay(delayMs, context.CancellationToken);
            }

            var size = param.Size;
            var body = new byte[size];

            for (var i = 0; i < size; i++)
            {
                body[i] = (byte)(i % 256);
            }

            var response = new StreamingOutputCallResponse
            {
                Payload = new Payload
                {
                    Type = request.ResponseType,
                    Body = ByteString.CopyFrom(body)
                }
            };

            await responseStream.WriteAsync(response, context.CancellationToken);
        }

        if (failLate.HasValue)
        {
            throw new RpcException(new Status(failLate.Value, "fail"));
        }
    }

    /// <summary>
    ///     A sequence of requests followed by one response (streamed upload).
    ///     The server returns the aggregated size of client payloads as the result.
    /// </summary>
    public override async Task<StreamingInputCallResponse> StreamingInputCall(
        IAsyncStreamReader<StreamingInputCallRequest> requestStream,
        ServerCallContext context)
    {
        var (headers, trailers, failEarly, failLate, delayMs) = MetadataProcessor.ProcessMetadata(context);

        await MetadataProcessor.SetResponseHeadersAsync(context, headers);

        MetadataProcessor.SetResponseTrailers(context, trailers);

        // Records how this handler unwound, for tests that need to see the client release the call
        // from the server's side. It observes only — the handler's own control flow is unchanged.
        var observeId = MetadataProcessor.GetObserveAbortId(context);

        try
        {
            if (failEarly.HasValue)
            {
                throw new RpcException(new Status(failEarly.Value, "fail"));
            }

            if (delayMs > 0)
            {
                await Task.Delay(delayMs, context.CancellationToken);
            }

            var totalSize = 0;

            await foreach (var request in requestStream.ReadAllAsync(context.CancellationToken))
            {
                context.CancellationToken.ThrowIfCancellationRequested();

                totalSize += request.Payload?.Body?.Length ?? 0;
            }

            if (failLate.HasValue)
            {
                throw new RpcException(new Status(failLate.Value, "fail"));
            }

            CallAbortObserver.Record(observeId, CallAbortObserver.Outcome.Drained);

            return new StreamingInputCallResponse
            {
                AggregatedPayloadSize = totalSize
            };
        }
        catch (Exception ex)
        {
            // An abandoned request stream reaches us as cancellation or as the IOException Kestrel
            // raises when the peer resets the stream; both mean the client let go of the call.
            var aborted = context.CancellationToken.IsCancellationRequested
                          || ex is OperationCanceledException or IOException;

            CallAbortObserver.Record(
                observeId,
                aborted ? CallAbortObserver.Outcome.Aborted : CallAbortObserver.Outcome.Faulted);

            throw;
        }
    }

    /// <summary>
    ///     A sequence of requests with each request served by the server immediately.
    ///     As one request could lead to multiple responses, this interface demonstrates
    ///     the idea of full duplexing.
    /// </summary>
    public override async Task FullDuplexCall(
        IAsyncStreamReader<StreamingOutputCallRequest> requestStream,
        IServerStreamWriter<StreamingOutputCallResponse> responseStream,
        ServerCallContext context)
    {
        var (headers, trailers, failEarly, failLate, delayMs) = MetadataProcessor.ProcessMetadata(context);
        var completeAfterRequests = MetadataProcessor.GetCompleteAfterRequests(context);

        await MetadataProcessor.SetResponseHeadersAsync(context, headers);

        MetadataProcessor.SetResponseTrailers(context, trailers);

        if (failEarly.HasValue)
        {
            throw new RpcException(new Status(failEarly.Value, "fail"));
        }

        var handled = 0;

        if (completeAfterRequests == handled)
        {
            // Zero: complete without reading anything at all.
            return;
        }

        await foreach (var request in requestStream.ReadAllAsync(context.CancellationToken))
        {
            context.CancellationToken.ThrowIfCancellationRequested();

            if (delayMs > 0)
            {
                await Task.Delay(delayMs, context.CancellationToken);
            }

            foreach (var size in request.ResponseParameters.Select(param => param.Size))
            {
                var body = new byte[size];

                for (var i = 0; i < size; i++)
                {
                    body[i] = (byte)(i % 256);
                }

                var response = new StreamingOutputCallResponse
                {
                    Payload = new Payload
                    {
                        Type = request.ResponseType,
                        Body = ByteString.CopyFrom(body)
                    }
                };

                await responseStream.WriteAsync(response, context.CancellationToken);
            }

            handled++;

            if (completeAfterRequests == handled)
            {
                // Close the response side with OK while the client's request producer is very
                // likely still running, deliberately leaving the request stream undrained.
                return;
            }
        }

        if (failLate.HasValue)
        {
            throw new RpcException(new Status(failLate.Value, "fail"));
        }
    }

    /// <summary>
    ///     A sequence of requests followed by a sequence of responses.
    ///     The server buffers all the client requests and then serves them in order.
    ///     A stream of responses is returned to the client once the client half-closes the stream.
    /// </summary>
    public override async Task HalfDuplexCall(
        IAsyncStreamReader<StreamingOutputCallRequest> requestStream,
        IServerStreamWriter<StreamingOutputCallResponse> responseStream,
        ServerCallContext context)
    {
        var (headers, trailers, failEarly, failLate, delayMs) = MetadataProcessor.ProcessMetadata(context);

        await MetadataProcessor.SetResponseHeadersAsync(context, headers);

        MetadataProcessor.SetResponseTrailers(context, trailers);

        if (failEarly.HasValue)
        {
            throw new RpcException(new Status(failEarly.Value, "fail"));
        }

        // Buffer all requests first
        var requests = new List<StreamingOutputCallRequest>();

        await foreach (var request in requestStream.ReadAllAsync(context.CancellationToken))
        {
            context.CancellationToken.ThrowIfCancellationRequested();
            requests.Add(request);
        }

        if (delayMs > 0)
        {
            await Task.Delay(delayMs, context.CancellationToken);
        }

        // Then send all responses
        foreach (var response in requests.Select(request => new StreamingOutputCallResponse
        {
            Payload = request.Payload
        }))
        {
            await responseStream.WriteAsync(response, context.CancellationToken);
        }

        if (failLate.HasValue)
        {
            throw new RpcException(new Status(failLate.Value, "fail"));
        }
    }
}
