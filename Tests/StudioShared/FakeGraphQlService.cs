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

    public Func<GraphQlExecutionRequest, CancellationToken, Task<GraphQlExecutionResult>>? OnExecute { get; set; }

    public GraphQlExecutionRequest? LastRequest { get; private set; }

    public int ExecuteCount { get; private set; }

    public GraphQlParseResult Parse(string document) => OnParse?.Invoke(document) ?? ParseResult;

    public Task<GraphQlExecutionResult> ExecuteAsync(GraphQlExecutionRequest request, CancellationToken cancellationToken)
    {
        LastRequest = request;
        ExecuteCount++;

        if (OnExecute is not null)
        {
            return OnExecute(request, cancellationToken);
        }

        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(ExecuteResult);
    }
}
