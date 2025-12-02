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
    ///     One request followed by one response. The server returns the client payload as-is.
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

        return new SimpleResponse
        {
            Payload = request.Payload
        };
    }

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

        return new StreamingInputCallResponse
        {
            AggregatedPayloadSize = totalSize
        };
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

        await MetadataProcessor.SetResponseHeadersAsync(context, headers);

        MetadataProcessor.SetResponseTrailers(context, trailers);

        if (failEarly.HasValue)
        {
            throw new RpcException(new Status(failEarly.Value, "fail"));
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