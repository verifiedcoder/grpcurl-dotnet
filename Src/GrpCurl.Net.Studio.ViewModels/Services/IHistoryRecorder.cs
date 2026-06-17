using GrpCurl.Net.Studio.ViewModels.Models.Invocation;

namespace GrpCurl.Net.Studio.ViewModels.Services;

/// <summary>
///     Records completed invocations into history (FR-120). Builds an already-redacted
///     <see cref="Models.History.HistoryEntry" /> from the request + outcome and appends it through
///     <see cref="IHistoryStore" />, honouring the capture/response settings. A no-op when capture is off.
/// </summary>
public interface IHistoryRecorder
{
    /// <summary>Records a unary/server-streaming-result invocation from its request and result.</summary>
    Task RecordUnaryAsync(InvocationRequestModel request, InvocationResultModel result, CancellationToken cancellationToken = default);

    /// <summary>Records a streaming invocation from its request, terminal status, and message counts.</summary>
    Task RecordStreamAsync(
        StreamRequestModel request, InvocationStatusModel status, long durationMs,
        int messagesSent, int messagesReceived, CancellationToken cancellationToken = default);
}
