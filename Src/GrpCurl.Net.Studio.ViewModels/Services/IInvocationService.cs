using Google.Protobuf;
using Google.Protobuf.Reflection;
using Grpc.Core;
using Grpc.Net.Client;
using GrpCurl.Net.Invocation;

namespace GrpCurl.Net.Studio.ViewModels.Services;

/// <summary>The gRPC status of a completed call (rich google.rpc.Status decoding arrives with E1.5).</summary>
public sealed record InvocationStatus(int Code, string CodeName, string Detail);

/// <summary>
///     The result of a unary call: success carries the response + metadata; an RPC failure is
///     captured (not thrown) so the UI and the conformance adapter handle it uniformly.
///     <see cref="RichDetails" /> carries any decoded <c>google.rpc.Status</c> details (E1.5).
/// </summary>
public sealed record UnaryOutcome(
    bool Ok,
    Metadata ResponseHeaders,
    IMessage? Response,
    Metadata? ResponseTrailers,
    InvocationStatus Status,
    StatusDetails? RichDetails = null);

/// <summary>
///     The conformance-drivable invocation core (SPEC-030 §4): a thin, Core-typed wrapper over
///     <c>DynamicInvoker</c> — the exact call path the Invoke button uses. Lives in the UI-free
///     view-model layer so the headless conformance adapter can drive it without Avalonia. RPC
///     errors are captured into <see cref="UnaryOutcome" />; only cancellation throws. Streaming
///     shapes (the <c>StreamEvent</c> pipeline, ADR-013) arrive with E2.1.
/// </summary>
public interface IInvocationService
{
    Task<UnaryOutcome> InvokeUnaryAsync(
        GrpcChannel channel,
        MethodDescriptor method,
        IMessage request,
        Metadata headers,
        DateTime? deadline,
        CancellationToken cancellationToken);

    IMessage CreateMessageFromJson(MessageDescriptor descriptor, string? json, bool allowUnknownFields = true);

    string MessageToJson(IMessage message, bool includeDefaults = false, bool indent = true);
}
