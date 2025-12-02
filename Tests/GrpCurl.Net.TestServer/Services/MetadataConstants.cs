namespace GrpCurl.Net.TestServer.Services;

/// <summary>
///     Metadata header constants for controlling test server behavior.
///     These match the Go grpcurl test server implementation.
/// </summary>
public static class MetadataConstants
{
    /// <summary>
    ///     Request header that contains values that will be echoed back to the client
    ///     as response headers. The format of the value is "key: val". To have the server
    ///     reply with more than one response header, supply multiple values in request metadata.
    /// </summary>
    public const string ReplyWithHeaders = "reply-with-headers";

    /// <summary>
    ///     Request header that contains values that will be echoed back to the client
    ///     as response trailers. Its format is the same as ReplyWithHeaders.
    /// </summary>
    public const string ReplyWithTrailers = "reply-with-trailers";

    /// <summary>
    ///     Request header that, if present and not zero, indicates that the RPC should
    ///     fail immediately with that code.
    /// </summary>
    public const string FailEarly = "fail-early";

    /// <summary>
    ///     Request header that, if present and not zero, indicates that the RPC should
    ///     fail at the end with that code. This is different from FailEarly only for
    ///     streaming calls. An early failure means the call fails before any request
    ///     stream is read or any response stream is generated. A late failure means
    ///     the entire request and response streams will be consumed/processed and only
    ///     then will the error code be sent.
    /// </summary>
    public const string FailLate = "fail-late";

    /// <summary>
    ///     Request header that, if present, adds a delay to the response in milliseconds.
    ///     Useful for testing timeouts.
    /// </summary>
    public const string DelayMs = "delay-ms";
}