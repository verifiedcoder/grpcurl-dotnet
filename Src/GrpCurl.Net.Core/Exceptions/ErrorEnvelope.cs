using System.Text.Json.Serialization;

namespace GrpCurl.Net.Exceptions;

/// <summary>
///     Structured error metadata carried by <see cref="Exceptions.GrpcCommandException" />
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