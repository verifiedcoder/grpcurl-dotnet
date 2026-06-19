using Gql2Grpc.Configuration;
using Gql2Grpc.Diagnostics;
using Gql2Grpc.Execution;
using Gql2Grpc.GraphQL;
using Gql2Grpc.Introspection;
using Gql2Grpc.Response;
using Gql2Grpc.Translation;
using Grpc.Core;
using GrpCurl.Net.DescriptorSources;
using GrpCurl.Net.Studio.ViewModels.Models.Connections;
using GrpCurl.Net.Studio.ViewModels.Models.GraphQl;
using GrpCurl.Net.Studio.ViewModels.Services;
using GrpCurl.Net.Utilities;
using System.Text.Json.Nodes;

namespace GrpCurl.Net.Studio.Services;

/// <summary>
///     Default <see cref="IGraphQlService" />: drives the <c>Gql2Grpc</c> bridge in-process against a
///     Studio connection. Channel/TLS/header/deadline handling mirrors <see cref="InvocationRunner" />
///     (reflection and the RPC share one channel, as the CLI does); the parse → coerce → resolve →
///     translate → execute graph is assembled exactly as the CLI's command handler does, so Studio and
///     <c>gql2grpc</c> run the same engine (GQL-007). PR-1 covers query/mutation execution; subscriptions
///     (E4.3), the mapping designer (E4.2), and introspection (E4.4) layer on later.
/// </summary>
internal sealed class GraphQlService(ITlsProfileResolver? tlsResolver = null, IEnvironmentService? environment = null) : IGraphQlService
{
    public GraphQlParseResult Parse(string document)
    {
        try
        {
            var parsed = GraphQLDocumentParser.Parse(document);

            var operations = parsed.Operations
                .Select(op => new GraphQlOperationInfo(op.Name, ToKind(op.OperationType)) { Variables = ToVariables(op) })
                .ToList();

            return new GraphQlParseResult(operations, []);
        }
        catch (Exception ex)
        {
            // The bridge parser throws ArgumentException (empty / no operations) and GraphQL-Parser throws
            // its syntax-error type; both are document-content problems to squiggle, never a Studio fault.
            return new GraphQlParseResult([], [ToSyntaxProblem(ex)]);
        }
    }

    public async Task<GraphQlExecutionResult> ExecuteAsync(
        GraphQlExecutionRequest request,
        IProgress<GraphQlFieldProgress>? progress,
        CancellationToken cancellationToken)
    {
        // ── Parse + select the operation (no network) ──────────────────────────
        GraphQLDocument document;
        GraphQLOperation operation;

        try
        {
            document = GraphQLDocumentParser.Parse(request.Document);
            operation = document.SelectOperation(request.OperationName);
        }
        catch (Exception ex)
        {
            // Document syntax, "no operations", or ambiguous-operation selection — all pre-RPC config errors.
            return new GraphQlExecutionResult(Ok: false, EnvelopeJson: null, [ToSyntaxProblem(ex)]);
        }

        // Subscriptions route to the streaming console in E4.3; reject here so the user gets a clear
        // message rather than the bridge's "exactly one root field" stream error.
        if (operation.OperationType == GraphQLOperationType.Subscription)
        {
            return ConfigError(GraphQlProblemKind.Configuration, "Subscriptions are not yet supported in Studio (coming in the subscriptions update).");
        }

        // ── Coerce variables (pre-RPC; AC-5) ───────────────────────────────────
        IReadOnlyList<ResolvedSelection> rootSelections;

        try
        {
            JsonNode? variablesFile = string.IsNullOrWhiteSpace(request.VariablesJson)
                ? null
                : VariableCoercer.ParseVariablesFile(request.VariablesJson);

            var variables = VariableCoercer.Coerce(operation.VariableDefinitions, cliVariables: null, variablesFile);
            var resolver = new SelectionResolver(document.Fragments, variables);
            rootSelections = resolver.Resolve(operation.SelectionSet);
        }
        catch (ArgumentException ex)
        {
            return ConfigError(GraphQlProblemKind.Variables, ex.Message);
        }

        // ── Build the bridge graph and execute ─────────────────────────────────
        var mappingConfig = await MappingConfigLoader.LoadAsync(request.MappingPath, cancellationToken).ConfigureAwait(false);
        var mappingResolver = new MappingResolver(mappingConfig, request.DefaultService);

        var connection = request.Connection;
        var (profile, password) = await ResolveTlsAsync(connection, cancellationToken).ConfigureAwait(false);
        var options = ConnectionChannelMapper.ToChannelOptions(connection, null, profile, password);
        var reflectionMetadata = ConnectionChannelMapper.BuildReflectionMetadata(connection);
        var (protosets, protos, imports) = ConnectionChannelMapper.DescriptorPaths(connection);

        await using var session = await DescriptorSourceFactory.CreateAsync(
            connection.Address, protosets, protos, imports,
            channelOptions: options,
            reflectionMetadata: reflectionMetadata,
            cancellationToken: cancellationToken).ConfigureAwait(false);

        if (session.Channel is not { } channel)
        {
            return ConfigError(GraphQlProblemKind.Configuration, "This connection has no target gRPC address; GraphQL execution requires a live server.");
        }

        Metadata rpcMetadata;

        try
        {
            rpcMetadata = GrpcChannelFactory.CreateMetadata(
                await ExpandHeadersAsync(request.Headers, cancellationToken).ConfigureAwait(false),
                NullIfBlank(connection.UserAgent));
        }
        catch (InvalidOperationException ex)
        {
            return ConfigError(GraphQlProblemKind.Configuration, ex.Message);
        }

        var executorOptions = new ExecutorOptions
        {
            RpcMetadata = rpcMetadata,
            Deadline = ParseDeadline(request.Deadline),
            EmitDefaults = request.EmitDefaults,
            AllowUnknownFields = request.AllowUnknownFields,
            RawOutput = request.Raw,
            IntrospectionEnabled = request.Introspection
        };

        var executor = new OperationExecutor(
            mappingResolver,
            session.Source,
            new GrpcTransport(channel),
            new JsonRequestTranslator(),
            new SelectionProjector(request.StrictSelection),
            new IntrospectionExecutor(new GraphQLSchemaBuilder(session.Source, mappingConfig), new SelectionProjector(request.StrictSelection)),
            executorOptions,
            new VerboseLogger(VerbosityLevel.Quiet));

        var bridgeProgress = progress is null ? null : new FieldProgressAdapter(progress);

        var envelope = await executor.ExecuteUnaryAsync(operation.OperationType, rootSelections, cancellationToken, bridgeProgress).ConfigureAwait(false);

        var json = GraphQLResponseBuilder.Serialize(envelope);
        var ok = envelope["errors"] is not JsonArray errors || errors.Count == 0;

        return new GraphQlExecutionResult(ok, json, []);
    }

    private static GraphQlExecutionResult ConfigError(GraphQlProblemKind kind, string message)
        => new(Ok: false, EnvelopeJson: null, [new GraphQlProblem(message, kind)]);

    /// <summary>Maps a parse failure to a syntax problem, carrying GraphQL-Parser's 1-based line/column when present.</summary>
    private static GraphQlProblem ToSyntaxProblem(Exception ex)
        => ex is GraphQLParser.Exceptions.GraphQLParserException pex
            ? new GraphQlProblem(pex.Message, GraphQlProblemKind.Syntax, pex.Location.Line, pex.Location.Column)
            : new GraphQlProblem(ex.Message, GraphQlProblemKind.Syntax);

    /// <summary>Maps the bridge's per-field progress onto the Studio model and forwards it to the VM sink.</summary>
    private sealed class FieldProgressAdapter(IProgress<GraphQlFieldProgress> sink) : IProgress<FieldExecutionProgress>
    {
        public void Report(FieldExecutionProgress value)
            => sink.Report(new GraphQlFieldProgress(value.FieldIndex, value.ResponseKey, ToState(value.State), value.Elapsed?.TotalMilliseconds));

        private static GraphQlFieldState ToState(FieldExecutionState state) => state switch
        {
            FieldExecutionState.Queued => GraphQlFieldState.Queued,
            FieldExecutionState.InFlight => GraphQlFieldState.InFlight,
            FieldExecutionState.Done => GraphQlFieldState.Done,
            FieldExecutionState.Failed => GraphQlFieldState.Failed,
            _ => GraphQlFieldState.Queued
        };
    }

    private static IReadOnlyList<GraphQlVariableInfo> ToVariables(GraphQLOperation op)
    {
        if (op.VariableDefinitions is null)
        {
            return [];
        }

        return op.VariableDefinitions
            .Select(v => new GraphQlVariableInfo(
                v.Variable.Name.StringValue,
                PrintType(v.Type),
                Required: v.Type is GraphQLParser.AST.GraphQLNonNullType && v.DefaultValue is null))
            .ToList();
    }

    private static string PrintType(GraphQLParser.AST.GraphQLType type) => type switch
    {
        GraphQLParser.AST.GraphQLNonNullType nn => PrintType(nn.Type) + "!",
        GraphQLParser.AST.GraphQLListType list => "[" + PrintType(list.Type) + "]",
        GraphQLParser.AST.GraphQLNamedType named => named.Name.StringValue,
        _ => "?"
    };

    private static GraphQlOperationKind ToKind(GraphQLOperationType type) => type switch
    {
        GraphQLOperationType.Query => GraphQlOperationKind.Query,
        GraphQLOperationType.Mutation => GraphQlOperationKind.Mutation,
        GraphQLOperationType.Subscription => GraphQlOperationKind.Subscription,
        _ => GraphQlOperationKind.Query
    };

    private async Task<(TlsProfile? Profile, string? Password)> ResolveTlsAsync(SavedConnection connection, CancellationToken cancellationToken)
        => tlsResolver is null ? default : await tlsResolver.ResolveAsync(connection, cancellationToken).ConfigureAwait(false);

    // Expand each header value's ${VAR} placeholders (active environment → OS), matching the invocation path.
    private async Task<IEnumerable<string>> ExpandHeadersAsync(IReadOnlyList<HeaderEntry> headers, CancellationToken cancellationToken)
    {
        var lines = new List<string>(headers.Count);

        foreach (var header in headers)
        {
            var value = environment is null
                ? header.Value
                : await environment.ExpandAsync(header.Value, cancellationToken).ConfigureAwait(false);
            lines.Add($"{header.Name}: {value}");
        }

        return lines;
    }

    private static DateTime? ParseDeadline(string? deadline)
        => string.IsNullOrWhiteSpace(deadline) ? null : DateTime.UtcNow.Add(GrpcChannelFactory.ParseDuration(deadline));

    private static string? NullIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;
}
