using System.Text.Json.Serialization;

namespace GrpCurl.Net.Commands;

/// <summary>
///     Discriminator for the cause of a CLI error. Drives both stderr text rendering
///     and the JSON envelope's <c>category</c> field.
/// </summary>
internal enum ErrorCategory
{
    /// <summary>Bad CLI args, missing required option, JSON parse error.</summary>
    Usage,

    /// <summary>Schema or file problem: protoset missing, symbol not found, file overwrite refused.</summary>
    Schema,

    /// <summary>Network failure before or around the gRPC call.</summary>
    Network,

    /// <summary>Connect or operation timeout.</summary>
    Timeout,

    /// <summary>RPC returned a non-OK status.</summary>
    Rpc,

    /// <summary>User interrupt (Ctrl+C).</summary>
    Cancelled,

    /// <summary>Unhandled or unknown error.</summary>
    Internal
}

/// <summary>
///     Structured error metadata carried by <see cref="Exceptions.GrpcCommandException"/>
///     and rendered to stderr as either Spectre.Console markup (text mode) or a one-line
///     JSON envelope (json mode).
/// </summary>
internal sealed record ErrorEnvelope
{
    public required ErrorCategory Category { get; init; }

    public required int ExitCode { get; init; }

    public required string Message { get; init; }

    public string? Hint { get; init; }

    public string? Address { get; init; }

    public string? Method { get; init; }

    public RpcErrorInfo? Grpc { get; init; }

    /// <summary>Per-line suggestion bullets printed under the error in text mode (omitted from JSON).</summary>
    [JsonIgnore]
    public IReadOnlyList<string> Suggestions { get; init; } = [];
}

/// <summary>
///     RPC-specific error sub-block. Present only when <see cref="ErrorEnvelope.Category"/> is
///     <see cref="ErrorCategory.Rpc"/>.
/// </summary>
internal sealed record RpcErrorInfo
{
    public required int Code { get; init; }

    public required string Status { get; init; }

    public required string Detail { get; init; }
}
