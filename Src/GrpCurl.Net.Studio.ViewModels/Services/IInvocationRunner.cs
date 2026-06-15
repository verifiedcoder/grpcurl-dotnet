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
}
