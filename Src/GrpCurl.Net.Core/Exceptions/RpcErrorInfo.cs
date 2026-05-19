namespace GrpCurl.Net.Exceptions;

/// <summary>
///     RPC-specific error sub-block. Present only when <see cref="ErrorEnvelope.Category" /> is
///     <see cref="ErrorCategory.Rpc" />.
/// </summary>
internal sealed record RpcErrorInfo
{
    public required int Code { get; init; }

    public required string Status { get; init; }

    public required string Detail { get; init; }

    /// <summary>
    ///     Decoded <c>grpc-status-details-bin</c> trailer payload (a <see cref="Google.Rpc.Status" />),
    ///     surfaced in the JSON error envelope when the server attaches rich details.
    /// </summary>
    public RpcStatusDetailsInfo? StatusDetails { get; init; }
}