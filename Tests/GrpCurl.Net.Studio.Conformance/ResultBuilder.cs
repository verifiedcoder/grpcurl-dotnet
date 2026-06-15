using Connectrpc.Conformance.V1;
using Google.Protobuf;
using Google.Protobuf.Collections;
using Google.Protobuf.Reflection;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using GrpCurl.Net.Invocation;

namespace GrpCurl.Net.Studio.Conformance;

/// <summary>
///     Maps gRPC-level results — <see cref="Metadata" />, response messages, and
///     <see cref="RpcException" /> — into the conformance result shapes. Identical to the CLI
///     adapter's mapper; the Studio adapter reconstructs an <see cref="RpcException" /> from the
///     captured <c>UnaryOutcome</c> so error/rich-detail handling round-trips the same way.
/// </summary>
internal static class ResultBuilder
{
    /// <summary>
    ///     Converts <see cref="Metadata" /> into conformance headers, grouping repeated
    ///     keys into one entry with ordered values and base64-encoding binary (-bin) values.
    /// </summary>
    public static void AddHeaders(RepeatedField<Header> target, Metadata? metadata)
    {
        if (metadata is null)
        {
            return;
        }

        var byName = new Dictionary<string, Header>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in metadata)
        {
            if (!byName.TryGetValue(entry.Key, out var header))
            {
                header = new Header { Name = entry.Key };

                byName[entry.Key] = header;
                target.Add(header);
            }

            header.Value.Add(entry.IsBinary ? Convert.ToBase64String(entry.ValueBytes) : entry.Value);
        }
    }

    /// <summary>
    ///     Extracts the ConformancePayload from a response message. The response arrives as the
    ///     product's SimpleDynamicMessage; serializing it back out and parsing with the generated
    ///     type round-trips GrpCurl.Net's ProtobufWriter.
    /// </summary>
    public static void AddPayload(RepeatedField<ConformancePayload> payloads, IMessage response, MethodDescriptor method)
    {
        var payloadField = method.OutputType.FindFieldByName("payload");

        if (payloadField is null)
        {
            // UnimplementedResponse has no payload field.
            return;
        }

        var typed = method.OutputType.Parser.ParseFrom(response.ToByteArray());

        // Every received response message yields exactly one payload entry; an unset
        // payload field still counts as an (empty) payload.
        payloads.Add(payloadField.Accessor.GetValue(typed) as ConformancePayload ?? new ConformancePayload());
    }

    /// <summary>
    ///     Maps an <see cref="RpcException" /> to the conformance error shape. Rich error details
    ///     are recovered from grpc-status-details-bin via the product's
    ///     <see cref="RichStatusDecoder" /> and re-wrapped as <see cref="Any" /> entries.
    /// </summary>
    public static void ApplyError(ClientResponseResult result, RpcException exception)
    {
        var error = new Error
        {
            // The conformance Code enum numerals match grpc StatusCode exactly.
            Code = (Code)(int)exception.StatusCode
        };

        if (!string.IsNullOrEmpty(exception.Status.Detail))
        {
            error.Message = exception.Status.Detail;
        }

        var rich = RichStatusDecoder.TryDecode(exception);

        if (rich is not null)
        {
            foreach (var detail in rich.Details)
            {
                error.Details.Add(new Any
                {
                    TypeUrl = detail.TypeUrl,
                    Value = ByteString.CopyFrom(detail.RawValue)
                });
            }
        }

        result.Error = error;

        // Unary errors arrive wrapped with the response headers the server sent before failing.
        if (exception is RpcInvocationException { ResponseHeaders: { } responseHeaders }
            && result.ResponseHeaders.Count == 0)
        {
            AddHeaders(result.ResponseHeaders, responseHeaders);
        }

        AddHeaders(result.ResponseTrailers, GetTrailersSafe(exception));
    }

    /// <summary>Maps a local cancellation to a canceled error result.</summary>
    public static void ApplyCanceled(ClientResponseResult result) =>
        result.Error = new Error { Code = Code.Canceled };

    private static Metadata? GetTrailersSafe(RpcException exception)
    {
        try
        {
            return exception.Trailers;
        }
        catch
        {
            return null;
        }
    }
}
