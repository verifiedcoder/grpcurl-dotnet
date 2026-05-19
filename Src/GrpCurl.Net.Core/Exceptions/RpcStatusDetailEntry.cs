namespace GrpCurl.Net.Exceptions;

internal sealed record RpcStatusDetailEntry
{
    public required string TypeUrl { get; init; }

    /// <summary>Base64-encoded raw value for unrecognised types.</summary>
    public string? RawBase64 { get; init; }

    /// <summary>Decoded payload as JSON when the type matched a google.rpc.* well-known.</summary>
    public string? Json { get; init; }
}