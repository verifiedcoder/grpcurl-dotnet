using Google.Protobuf;
using Grpc.Core;
using GrpCurl.Net.Invocation;

namespace GrpCurl.Net.Studio.ViewModels.Services;

/// <summary>
///     One event in a streaming RPC, produced by <see cref="IInvocationService.InvokeStreamingAsync" />
///     (ADR-013). The merged, ordered event stream covers both directions — received messages,
///     sent messages, headers, the terminal status, and warnings — so the UI and the conformance
///     adapter consume one chronological sequence. Carries only public gRPC/protobuf types (the same
///     ones <see cref="UnaryOutcome" /> exposes); Core's internal streaming-result types never leak.
/// </summary>
public abstract record StreamEvent
{
    /// <summary>Milliseconds since the call started (monotonic).</summary>
    public long ElapsedMs { get; init; }

    /// <summary>Wall-clock time the event was produced.</summary>
    public DateTimeOffset WallClock { get; init; }
}

/// <summary>Response headers arrived (once, before the first message).</summary>
public sealed record HeadersReceived(Metadata Headers) : StreamEvent;

/// <summary>A response message arrived. <see cref="Message" /> is the raw message (formatted lazily).</summary>
public sealed record MessageReceived(long Index, IMessage Message) : StreamEvent;

/// <summary>A request message was written to the wire (client/duplex composer or conformance source).</summary>
public sealed record MessageSent(long Index, IMessage Message) : StreamEvent;

/// <summary>
///     The terminal event for every shape: success carries <see cref="StatusCode.OK" />, a failure
///     carries the captured status + trailers + decoded rich <c>google.rpc.Status</c> details. RPC
///     errors are captured here (never thrown); cancellation surfaces as <see cref="StatusCode.Cancelled" />.
/// </summary>
public sealed record StatusReceived(InvocationStatus Status, Metadata? Trailers, StatusDetails? RichDetails = null) : StreamEvent;

/// <summary>A non-fatal advisory (e.g. a request message that failed local conversion was skipped).</summary>
public sealed record StreamWarning(string Message) : StreamEvent;
