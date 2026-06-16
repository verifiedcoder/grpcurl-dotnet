using Google.Protobuf;
using GrpCurl.Net.Studio.ViewModels.Models.Connections;

namespace GrpCurl.Net.Studio.ViewModels.Models.Invocation;

/// <summary>The kind of a streaming log row (drives glyph/colour and which fields are populated).</summary>
public enum StreamEventKind
{
    Headers,
    MessageReceived,
    MessageSent,
    Status,
    Warning
}

/// <summary>
///     A model-side streaming event (FR-081). Message rows keep the raw <see cref="RawMessage" /> so
///     the full body is formatted lazily on expand/export; <see cref="Preview" /> is a cheap one-line
///     summary. Meta rows (headers/status/warning) carry <see cref="Metadata" />/<see cref="Status" />/
///     <see cref="Error" /> instead. No Core/gRPC types beyond the raw <see cref="IMessage" /> reach the VM.
/// </summary>
public sealed record StreamEventModel(
    StreamEventKind Kind,
    long Index,
    DateTimeOffset WallClock,
    long ElapsedMs,
    string Preview,
    IMessage? RawMessage = null,
    IReadOnlyList<MetadataItem>? Metadata = null,
    InvocationStatusModel? Status = null,
    ErrorModel? Error = null);

/// <summary>A streaming invoke request: connection + method + headers + options (no per-call body — bodies stream in).</summary>
public sealed record StreamRequestModel(
    SavedConnection Connection,
    string MethodSymbol,
    IReadOnlyList<HeaderEntry> Headers,
    string? Deadline = null,
    bool EmitDefaults = false,
    bool AllowUnknownFields = true,
    string? MaxMessageSize = null);
