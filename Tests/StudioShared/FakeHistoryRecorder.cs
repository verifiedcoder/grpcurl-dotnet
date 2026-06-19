using GrpCurl.Net.Studio.ViewModels.Models.GraphQl;
using GrpCurl.Net.Studio.ViewModels.Models.Invocation;
using GrpCurl.Net.Studio.ViewModels.Services;

namespace GrpCurl.Net.Studio.TestSupport;

/// <summary>Records the history calls it receives so VM tests can assert what was captured.</summary>
public sealed class FakeHistoryRecorder : IHistoryRecorder
{
    public List<GraphQlHistoryContext> GraphQlRecords { get; } = [];

    public GraphQlHistoryContext? LastGraphQl => GraphQlRecords.Count == 0 ? null : GraphQlRecords[^1];

    public int UnaryCount { get; private set; }

    public int StreamCount { get; private set; }

    public Task RecordUnaryAsync(InvocationRequestModel request, InvocationResultModel result, CancellationToken cancellationToken = default)
    {
        UnaryCount++;
        return Task.CompletedTask;
    }

    public Task RecordStreamAsync(
        StreamRequestModel request, InvocationStatusModel status, long durationMs,
        int messagesSent, int messagesReceived, CancellationToken cancellationToken = default)
    {
        StreamCount++;
        return Task.CompletedTask;
    }

    public Task RecordGraphQlAsync(GraphQlHistoryContext context, CancellationToken cancellationToken = default)
    {
        GraphQlRecords.Add(context);
        return Task.CompletedTask;
    }
}
