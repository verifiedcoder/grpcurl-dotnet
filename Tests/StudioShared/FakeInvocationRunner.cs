using GrpCurl.Net.Studio.ViewModels.Models.Invocation;
using GrpCurl.Net.Studio.ViewModels.Services;

namespace GrpCurl.Net.Studio.TestSupport;

/// <summary>Scripted <see cref="IInvocationRunner" />: returns a canned result or a custom handler.</summary>
public sealed class FakeInvocationRunner : IInvocationRunner
{
    public InvocationResultModel Result { get; set; } = new(
        Ok: true, ResponseJson: "{}", ResponseHeaders: [], ResponseTrailers: [],
        Status: new InvocationStatusModel(0, "OK", string.Empty),
        Timing: new TimingModel([], 0, 0), ErrorMessage: null);

    public Func<InvocationRequestModel, CancellationToken, Task<InvocationResultModel>>? OnInvoke { get; set; }

    public InvocationRequestModel? LastRequest { get; private set; }

    public int InvokeCount { get; private set; }

    public Task<InvocationResultModel> InvokeUnaryAsync(InvocationRequestModel request, CancellationToken cancellationToken)
    {
        LastRequest = request;
        InvokeCount++;

        if (OnInvoke is not null)
        {
            return OnInvoke(request, cancellationToken);
        }

        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(Result);
    }
}
