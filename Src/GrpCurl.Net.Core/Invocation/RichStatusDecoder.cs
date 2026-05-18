using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Google.Rpc;
using Grpc.Core;

namespace GrpCurl.Net.Invocation;

/// <summary>
///     Decodes the <c>grpc-status-details-bin</c> trailer (a base64-encoded
///     <see cref="Google.Rpc.Status"/>) and surfaces each <see cref="Any"/> detail
///     payload either typed (when its <c>type_url</c> matches a well-known type) or as
///     a raw <c>{type_url, base64_value}</c> envelope so the caller can decode it out of
///     band. This is the upstream-grpcurl behaviour for <c>-format json</c> error output.
/// </summary>
public static class RichStatusDecoder
{
    public const string TrailerName = "grpc-status-details-bin";

    /// <summary>
    ///     Extracts and decodes the <c>grpc-status-details-bin</c> trailer from an
    ///     <see cref="RpcException"/>. Returns <see langword="null"/> when the trailer is
    ///     absent or unparseable.
    /// </summary>
    public static StatusDetails? TryDecode(RpcException exception)
    {
        Metadata? trailers;

        try
        {
            trailers = exception.Trailers;
        }
        catch
        {
            return null;
        }

        if (trailers is null)
        {
            return null;
        }

        var entry = trailers.FirstOrDefault(e =>
            string.Equals(e.Key, TrailerName, StringComparison.OrdinalIgnoreCase));

        if (entry is null || !entry.IsBinary)
        {
            return null;
        }

        return TryDecodeBytes(entry.ValueBytes);
    }

    /// <summary>
    ///     Parses a raw <see cref="Google.Rpc.Status"/> payload (the value of
    ///     <c>grpc-status-details-bin</c>) into a <see cref="StatusDetails"/>. Returns
    ///     <see langword="null"/> if the payload is unparseable.
    /// </summary>
    public static StatusDetails? TryDecodeBytes(byte[] payload)
    {
        try
        {
            var status = Google.Rpc.Status.Parser.ParseFrom(payload);
            var details = new List<StatusDetail>(status.Details.Count);

            foreach (var any in status.Details)
            {
                details.Add(DecodeAny(any));
            }

            return new StatusDetails(status.Code, status.Message, details);
        }
        catch (InvalidProtocolBufferException)
        {
            return null;
        }
    }

    private static StatusDetail DecodeAny(Any any)
    {
        var typeUrl = any.TypeUrl ?? string.Empty;
        var typeName = typeUrl.Contains('/') ? typeUrl[(typeUrl.LastIndexOf('/') + 1)..] : typeUrl;
        var rawValue = any.Value.ToByteArray();

        // Try the small set of well-known google.rpc.* detail types so callers see
        // structured fields rather than opaque base64. Anything else is surfaced raw.
        return typeName switch
        {
            "google.rpc.ErrorInfo" => TryUnpack(any, ErrorInfo.Parser, typeUrl, rawValue),
            "google.rpc.RetryInfo" => TryUnpack(any, RetryInfo.Parser, typeUrl, rawValue),
            "google.rpc.DebugInfo" => TryUnpack(any, DebugInfo.Parser, typeUrl, rawValue),
            "google.rpc.QuotaFailure" => TryUnpack(any, QuotaFailure.Parser, typeUrl, rawValue),
            "google.rpc.PreconditionFailure" => TryUnpack(any, PreconditionFailure.Parser, typeUrl, rawValue),
            "google.rpc.BadRequest" => TryUnpack(any, BadRequest.Parser, typeUrl, rawValue),
            "google.rpc.RequestInfo" => TryUnpack(any, RequestInfo.Parser, typeUrl, rawValue),
            "google.rpc.ResourceInfo" => TryUnpack(any, ResourceInfo.Parser, typeUrl, rawValue),
            "google.rpc.Help" => TryUnpack(any, Help.Parser, typeUrl, rawValue),
            "google.rpc.LocalizedMessage" => TryUnpack(any, LocalizedMessage.Parser, typeUrl, rawValue),
            _ => new StatusDetail(typeUrl, rawValue, ParsedMessage: null),
        };
    }

    private static StatusDetail TryUnpack<T>(Any any, MessageParser<T> parser, string typeUrl, byte[] rawValue)
        where T : IMessage<T>
    {
        try
        {
            var parsed = parser.ParseFrom(any.Value);

            return new StatusDetail(typeUrl, rawValue, parsed);
        }
        catch (InvalidProtocolBufferException)
        {
            return new StatusDetail(typeUrl, rawValue, ParsedMessage: null);
        }
    }
}

/// <summary>
///     Decoded form of <c>grpc-status-details-bin</c> — a status code, message, and a
///     list of <see cref="StatusDetail"/> payloads (each either typed against a
///     google.rpc.* well-known type or surfaced as raw bytes).
/// </summary>
public sealed record StatusDetails(int Code, string Message, IReadOnlyList<StatusDetail> Details);

/// <summary>One detail entry from a <see cref="StatusDetails"/>.</summary>
public sealed record StatusDetail(string TypeUrl, byte[] RawValue, IMessage? ParsedMessage);
