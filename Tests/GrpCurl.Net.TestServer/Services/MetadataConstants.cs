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

    /// <summary>
    ///     Request header that, if present and non-negative, makes a bidi-streaming RPC return
    ///     successfully once it has answered that many request messages, without reading the
    ///     rest of the request stream. Unlike <see cref="FailEarly" /> the call completes with
    ///     OK, which is what reproduces a client whose request producer is still running after
    ///     the server has closed its response side. Zero completes without reading anything.
    /// </summary>
    public const string CompleteAfterRequests = "complete-after-requests";

    /// <summary>
    ///     Request header carrying a correlation id previously registered with
    ///     <see cref="CallAbortObserver" />. The handler records how it unwound against that id and
    ///     nothing else — it grants the server no way to end the call on its own, so a test that
    ///     waits for an <see cref="CallAbortObserver.Outcome.Aborted" /> result is waiting for the
    ///     client to reset the stream and for no other reason (PRD-004).
    /// </summary>
    public const string ObserveAbortId = "observe-abort-id";
}