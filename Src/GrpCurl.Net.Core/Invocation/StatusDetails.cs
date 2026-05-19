namespace GrpCurl.Net.Invocation;

/// <summary>
///     Decoded form of <c>grpc-status-details-bin</c> — a status code, message, and a
///     list of <see cref="StatusDetail" /> payloads (each either typed against a
///     google.rpc.* well-known type or surfaced as raw bytes).
/// </summary>
public sealed record StatusDetails(int Code, string Message, IReadOnlyList<StatusDetail> Details);