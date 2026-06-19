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

    /// <summary>Scripted subscription envelopes; a custom handler overrides them.</summary>
    public IReadOnlyList<string> StreamEnvelopes { get; set; } = [];

    public Func<GraphQlExecutionRequest, CancellationToken, IAsyncEnumerable<string>>? OnStream { get; set; }

    public GraphQlExecutionRequest? LastStreamRequest { get; private set; }

    public int StreamCount { get; private set; }

    public IAsyncEnumerable<string> StreamAsync(GraphQlExecutionRequest request, CancellationToken cancellationToken)
    {
        LastStreamRequest = request;
        StreamCount++;

        return OnStream is not null ? OnStream(request, cancellationToken) : Emit(StreamEnvelopes, cancellationToken);
    }

    private static async IAsyncEnumerable<string> Emit(
        IReadOnlyList<string> lines,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        foreach (var line in lines)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return line;
            await Task.Yield();
        }
    }

    public GraphQlSchemaResult SchemaResult { get; set; } = new(Ok: true, "Schema", [], "{}", Error: null);

    public Func<GraphQlExecutionRequest, CancellationToken, Task<GraphQlSchemaResult>>? OnIntrospect { get; set; }

    public int IntrospectCount { get; private set; }

    public Task<GraphQlSchemaResult> IntrospectAsync(GraphQlExecutionRequest request, CancellationToken cancellationToken)
    {
        IntrospectCount++;
        return OnIntrospect is not null ? OnIntrospect(request, cancellationToken) : Task.FromResult(SchemaResult);
    }

    public GraphQlResolutionResult ResolutionResult { get; set; } = new([], DefaultServiceOverridden: false, OverriddenService: null);

    public Func<GraphQlExecutionRequest, CancellationToken, Task<GraphQlResolutionResult>>? OnResolve { get; set; }

    public int ResolveCount { get; private set; }

    public Task<GraphQlResolutionResult> ResolveAsync(GraphQlExecutionRequest request, CancellationToken cancellationToken)
    {
        ResolveCount++;
        return OnResolve is not null ? OnResolve(request, cancellationToken) : Task.FromResult(ResolutionResult);
    }

    public IReadOnlyList<GraphQlProblem> MappingProblems { get; set; } = [];

    public Func<string, IReadOnlyList<GraphQlProblem>>? OnValidateMapping { get; set; }

    public string? LastValidatedMapping { get; private set; }

    public IReadOnlyList<GraphQlProblem> ValidateMapping(string mappingText)
    {
        LastValidatedMapping = mappingText;
        return OnValidateMapping?.Invoke(mappingText) ?? MappingProblems;
    }

    public GraphQlTranslationResult TranslationResult { get; set; } = new([]);

    public Func<GraphQlExecutionRequest, CancellationToken, Task<GraphQlTranslationResult>>? OnTranslate { get; set; }

    public int TranslateCount { get; private set; }

    public Task<GraphQlTranslationResult> TranslateAsync(GraphQlExecutionRequest request, CancellationToken cancellationToken)
    {
        TranslateCount++;
        return OnTranslate is not null ? OnTranslate(request, cancellationToken) : Task.FromResult(TranslationResult);
    }
}
