using GrpCurl.Net.Studio.ViewModels.Models.GraphQl;
using GrpCurl.Net.Studio.ViewModels.Services;

namespace GrpCurl.Net.Studio.TestSupport;

/// <summary>
///     Scripted <see cref="IGraphQlService" />: returns canned parse/execute results or custom handlers,
///     and records the last execute request — lets the GraphQL document VM be tested without the bridge.
/// </summary>
public sealed class FakeGraphQlService : IGraphQlService
{
    public GraphQlParseResult ParseResult { get; set; } = new([], []);

    public Func<string, GraphQlParseResult>? OnParse { get; set; }

    public GraphQlExecutionResult ExecuteResult { get; set; } = new(Ok: true, EnvelopeJson: "{\n  \"data\": {}\n}", ConfigurationErrors: []);

    public Func<GraphQlExecutionRequest, IProgress<GraphQlFieldProgress>?, CancellationToken, Task<GraphQlExecutionResult>>? OnExecute { get; set; }

    public GraphQlExecutionRequest? LastRequest { get; private set; }

    /// <summary>Per-field progress notifications to emit before returning (lets VM progress wiring be tested).</summary>
    public IReadOnlyList<GraphQlFieldProgress> ProgressEvents { get; set; } = [];

    public int ExecuteCount { get; private set; }

    public GraphQlParseResult Parse(string document) => OnParse?.Invoke(document) ?? ParseResult;

    public Task<GraphQlExecutionResult> ExecuteAsync(
        GraphQlExecutionRequest request,
        IProgress<GraphQlFieldProgress>? progress,
        CancellationToken cancellationToken)
    {
        LastRequest = request;
        ExecuteCount++;

        foreach (var ev in ProgressEvents)
        {
            progress?.Report(ev);
        }

        if (OnExecute is not null)
        {
            return OnExecute(request, progress, cancellationToken);
        }

        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(ExecuteResult);
    }
}
