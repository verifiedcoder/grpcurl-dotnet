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
    ///     Returns the correlation id a test registered with <see cref="CallAbortObserver" />, or null
    ///     when the caller is not observing this call.
    /// </summary>
    public static string? GetObserveAbortId(ServerCallContext context)
    {
        foreach (var entry in context.RequestHeaders)
        {
            if (string.Equals(entry.Key, MetadataConstants.ObserveAbortId, StringComparison.OrdinalIgnoreCase))
            {
                return entry.Value;
            }
        }

        return null;
    }

    /// <summary>
    ///     Reads <see cref="MetadataConstants.CompleteAfterRequests" />, returning
    ///     <see langword="null" /> when absent, unparseable, or not positive. Kept separate from
    ///     <see cref="ProcessMetadata" /> because only the bidi handler honours it.
    /// </summary>
    public static int? GetCompleteAfterRequests(ServerCallContext context)
    {
        foreach (var entry in context.RequestHeaders)
        {
            if (!string.Equals(entry.Key, MetadataConstants.CompleteAfterRequests, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            // Zero is meaningful: complete without reading a single request, which is valid duplex
            // behaviour and the only way to exercise a client whose request half never gets going.
            if (int.TryParse(entry.Value, out var count) && count >= 0)
            {
                return count;
            }
        }

        return null;
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