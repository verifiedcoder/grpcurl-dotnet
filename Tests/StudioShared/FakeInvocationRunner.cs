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

    /// <summary>Scripted stream events; the default is empty (no rows).</summary>
    public IReadOnlyList<StreamEventModel> StreamEvents { get; set; } = [];

    public Func<StreamRequestModel, IAsyncEnumerable<string>, CancellationToken, IAsyncEnumerable<StreamEventModel>>? OnStream { get; set; }

    public StreamRequestModel? LastStreamRequest { get; private set; }

    public int StreamCount { get; private set; }

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

    public IAsyncEnumerable<StreamEventModel> InvokeStreamingAsync(
        StreamRequestModel request, IAsyncEnumerable<string> requestJson, CancellationToken cancellationToken)
    {
        LastStreamRequest = request;
        StreamCount++;

        return OnStream is not null ? OnStream(request, requestJson, cancellationToken) : Canned(cancellationToken);
    }

    private async IAsyncEnumerable<StreamEventModel> Canned(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        foreach (var ev in StreamEvents)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return ev;
            await Task.Yield();
        }
    }
}
