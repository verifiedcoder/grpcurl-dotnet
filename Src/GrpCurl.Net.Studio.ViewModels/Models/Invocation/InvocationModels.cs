using GrpCurl.Net.Studio.ViewModels.Models.Connections;

namespace GrpCurl.Net.Studio.ViewModels.Models.Invocation;

/// <summary>A unary invoke request expressed in UI-friendly terms (no Core types).</summary>
public sealed record InvocationRequestModel(
    SavedConnection Connection,
    string MethodSymbol,
    string RequestJson,
    IReadOnlyList<HeaderEntry> Headers,
    string? Deadline = null,
    bool EmitDefaults = false,
    bool AllowUnknownFields = true,
    string? MaxMessageSize = null);

/// <summary>A response/trailing metadata entry (binary <c>-bin</c> values are base64 in <see cref="Value" />).</summary>
public sealed record MetadataItem(string Name, string Value, bool IsBinary);

/// <summary>
///     An advisory request-validation problem (FR-063). <see cref="Line" />/<see cref="Column" /> are
///     1-based and present only for JSON syntax errors; semantic errors carry just a message.
/// </summary>
public sealed record ValidationProblem(string Message, int? Line, int? Column)
{
    public string Display => Line is { } line ? $"{Message} (line {line})" : Message;
}

/// <summary>The gRPC status of a completed call, model-side.</summary>
public sealed record InvocationStatusModel(int Code, string CodeName, string Detail);

/// <summary>One timing phase and its duration.</summary>
public sealed record TimingPhase(string Phase, TimeSpan Duration);

/// <summary>Timing breakdown for a call (feeds the Timing tab).</summary>
public sealed record TimingModel(IReadOnlyList<TimingPhase> Phases, long RequestBytes, long ResponseBytes);

/// <summary>
///     The model-side result of a unary invoke: response JSON + metadata + status + timing. On
///     failure, <see cref="Error" /> carries the rich error presentation (FR-090..099); the legacy
///     <see cref="ErrorMessage" /> is retained as the one-line headline.
/// </summary>
public sealed record InvocationResultModel(
    bool Ok,
    string? ResponseJson,
    IReadOnlyList<MetadataItem> ResponseHeaders,
    IReadOnlyList<MetadataItem> ResponseTrailers,
    InvocationStatusModel Status,
    TimingModel Timing,
    string? ErrorMessage,
    ErrorModel? Error = null);
