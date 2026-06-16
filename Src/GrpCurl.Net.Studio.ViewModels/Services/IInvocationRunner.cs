using GrpCurl.Net.Studio.ViewModels.Models.Invocation;

namespace GrpCurl.Net.Studio.ViewModels.Services;

/// <summary>
///     The model-facing invocation orchestration the invocation tab calls. Resolves the channel
///     and method descriptor for a connection, builds the request/metadata/deadline, runs the call
///     through <see cref="IInvocationService" />, and maps the outcome to UI model types — keeping
///     raw Core/gRPC types out of the view models. User cancellation surfaces as
///     <see cref="OperationCanceledException" />.
/// </summary>
public interface IInvocationRunner
{
    Task<InvocationResultModel> InvokeUnaryAsync(InvocationRequestModel request, CancellationToken cancellationToken);

    /// <summary>
    ///     Drives a streaming RPC and yields UI-model events (FR-080..084). Request bodies are fed as
    ///     JSON strings via <paramref name="requestJson" /> (the composer's channel; a single-element
    ///     source for server-streaming). Each yielded <see cref="StreamEventModel" /> is a log row.
    ///     User cancellation surfaces as <see cref="OperationCanceledException" /> after already-yielded
    ///     rows are preserved.
    /// </summary>
    IAsyncEnumerable<StreamEventModel> InvokeStreamingAsync(
        StreamRequestModel request,
        IAsyncEnumerable<string> requestJson,
        CancellationToken cancellationToken);

    /// <summary>Pretty-prints a retained streaming message on demand (FR-081 lazy formatting).</summary>
    string FormatMessage(Google.Protobuf.IMessage message);
}
