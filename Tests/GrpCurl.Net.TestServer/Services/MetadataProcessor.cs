using Grpc.Core;

namespace GrpCurl.Net.TestServer.Services;

/// <summary>
///     Processes incoming metadata to extract test control headers.
/// </summary>
public static class MetadataProcessor
{
    /// <summary>
    ///     Processes incoming metadata and returns control values for test behavior.
    /// </summary>
    public static (Metadata headers, Metadata trailers, StatusCode? failEarly, StatusCode? failLate, int delayMs)
        ProcessMetadata(ServerCallContext context)
    {
        StatusCode? failEarly = null;
        StatusCode? failLate = null;

        var requestHeaders = context.RequestHeaders;
        var replyHeaders = new Metadata();
        var replyTrailers = new Metadata();
        var delayMs = 0;

        foreach (var entry in requestHeaders)
        {
            switch (entry.Key.ToLowerInvariant())
            {
                case MetadataConstants.ReplyWithHeaders:

                    ParseHeaderValue(entry.Value, replyHeaders);

                    break;

                case MetadataConstants.ReplyWithTrailers:

                    ParseHeaderValue(entry.Value, replyTrailers);

                    break;

                case MetadataConstants.FailEarly:

                    if (int.TryParse(entry.Value, out var earlyCode) && earlyCode != 0)
                    {
                        failEarly = (StatusCode)earlyCode;
                    }

                    break;

                case MetadataConstants.FailLate:

                    if (int.TryParse(entry.Value, out var lateCode) && lateCode != 0)
                    {
                        failLate = (StatusCode)lateCode;
                    }

                    break;

                case MetadataConstants.DelayMs:

                    _ = int.TryParse(entry.Value, out delayMs);

                    break;
            }
        }

        return (replyHeaders, replyTrailers, failEarly, failLate, delayMs);
    }

    /// <summary>
    ///     Parses a header value in the format "key: value" and adds it to the metadata collection.
    /// </summary>
    private static void ParseHeaderValue(string value, Metadata metadata)
    {
        var colonIndex = value.IndexOf(':');

        if (colonIndex <= 0)
        {
            return;
        }

        var key = value[..colonIndex].Trim();
        var val = value[(colonIndex + 1)..].Trim();

        metadata.Add(key, val);
    }

    /// <summary>
    ///     Sets response headers on the context.
    /// </summary>
    public static async Task SetResponseHeadersAsync(ServerCallContext context, Metadata headers)
    {
        if (headers.Count > 0)
        {
            await context.WriteResponseHeadersAsync(headers);
        }
    }

    /// <summary>
    ///     Sets response trailers on the context.
    /// </summary>
    public static void SetResponseTrailers(ServerCallContext context, Metadata trailers)
    {
        foreach (var entry in trailers)
        {
            context.ResponseTrailers.Add(entry);
        }
    }
}