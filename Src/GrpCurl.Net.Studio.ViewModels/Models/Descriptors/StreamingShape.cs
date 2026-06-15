namespace GrpCurl.Net.Studio.ViewModels.Models.Descriptors;

/// <summary>The four gRPC method shapes carried as badges on method nodes (FR-021).</summary>
public enum StreamingShape
{
    Unary,
    ServerStreaming,
    ClientStreaming,
    BidiStreaming
}

/// <summary>Presentation helpers for <see cref="StreamingShape" />: badge text and accessible labels.</summary>
public static class StreamingShapeExtensions
{
    /// <summary>Short badge text shown on a method node: U / SS / CS / BD.</summary>
    public static string Badge(this StreamingShape shape) => shape switch
    {
        StreamingShape.Unary => "U",
        StreamingShape.ServerStreaming => "SS",
        StreamingShape.ClientStreaming => "CS",
        StreamingShape.BidiStreaming => "BD",
        _ => "?"
    };

    /// <summary>Full label used for tooltips and accessible names (SPEC-020 §6).</summary>
    public static string Label(this StreamingShape shape) => shape switch
    {
        StreamingShape.Unary => "Unary",
        StreamingShape.ServerStreaming => "Server streaming",
        StreamingShape.ClientStreaming => "Client streaming",
        StreamingShape.BidiStreaming => "Bidirectional streaming",
        _ => "Unknown"
    };

    /// <summary>Derives the shape from a method descriptor's streaming flags.</summary>
    public static StreamingShape FromFlags(bool clientStreaming, bool serverStreaming) => (clientStreaming, serverStreaming) switch
    {
        (false, false) => StreamingShape.Unary,
        (false, true) => StreamingShape.ServerStreaming,
        (true, false) => StreamingShape.ClientStreaming,
        (true, true) => StreamingShape.BidiStreaming
    };
}
