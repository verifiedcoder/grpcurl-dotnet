namespace GrpCurl.Net.Exceptions;

// Property getters are read by System.Text.Json via reflection in ErrorRenderer.RenderJson.
// ReSharper disable UnusedAutoPropertyAccessor.Global
internal sealed record RpcStatusDetailsInfo
{
    public required int Code { get; init; }

    public required string Message { get; init; }

    public required IReadOnlyList<RpcStatusDetailEntry> Details { get; init; }
}