namespace GrpCurl.Net.Exceptions;

internal sealed record RpcStatusDetailsInfo
{
    public required int Code { get; init; }

    public required string Message { get; init; }

    public required IReadOnlyList<RpcStatusDetailEntry> Details { get; init; }
}