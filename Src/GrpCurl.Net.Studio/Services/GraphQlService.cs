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
using static System.FormattableString;

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
                .Select(op => new GraphQlOperationInfo(op.Name, ToKind(op.OperationType))
                {
                    Variables = ToVariables(op),
                    RootFieldCount = RootFieldCount(op)
                })
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
        // ── Parse + select + coerce (no network; AC-5) ─────────────────────────
        var (operation, rootSelections, prepError) = PrepareSelections(request);

        if (prepError is not null || operation is null || rootSelections is null)
        {
            return new GraphQlExecutionResult(Ok: false, EnvelopeJson: null, [prepError!]);
        }

        // ── Build the bridge graph and execute ─────────────────────────────────
        var mappingConfig = await LoadMappingAsync(request, cancellationToken).ConfigureAwait(false);
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

        // GQL-029: when the verbose pane is on, capture the bridge's VerboseLogger lines (plain text, no
        // stderr/markup) so the host renders the same per-field resolved mapping (and request JSON at -vv).
        var captured = new System.Collections.Concurrent.ConcurrentQueue<string>();
        var logger = request.Verbosity == GraphQlVerbosity.Off
            ? new VerboseLogger(VerbosityLevel.Quiet)
            : new VerboseLogger(ToVerbosity(request.Verbosity), captured.Enqueue);

        var executor = new OperationExecutor(
            mappingResolver,
            session.Source,
            new GrpcTransport(channel),
            new JsonRequestTranslator(),
            new SelectionProjector(request.StrictSelection),
            new IntrospectionExecutor(new GraphQLSchemaBuilder(session.Source, mappingConfig), new SelectionProjector(request.StrictSelection)),
            executorOptions,
            logger);

        var bridgeProgress = progress is null ? null : new FieldProgressAdapter(progress);

        var envelope = await executor.ExecuteUnaryAsync(operation.OperationType, rootSelections, cancellationToken, bridgeProgress).ConfigureAwait(false);

        var json = GraphQLResponseBuilder.Serialize(envelope);
        var ok = envelope["errors"] is not JsonArray errors || errors.Count == 0;

        return new GraphQlExecutionResult(ok, json, [])
        {
            VerboseLog = [.. captured],
            Errors = ParseErrors(envelope)
        };
    }

    public async IAsyncEnumerable<string> StreamAsync(
        GraphQlExecutionRequest request,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var (operation, rootSelections, prepError) = PrepareSelections(request);

        if (prepError is not null || operation is null || rootSelections is null)
        {
            yield return ErrorEnvelope(prepError!.Message);
            yield break;
        }

        var mappingConfig = await LoadMappingAsync(request, cancellationToken).ConfigureAwait(false);
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
            yield return ErrorEnvelope("This connection has no target gRPC address; GraphQL execution requires a live server.");
            yield break;
        }

        Metadata rpcMetadata;
        string? metadataError = null;
        try
        {
            rpcMetadata = GrpcChannelFactory.CreateMetadata(
                await ExpandHeadersAsync(request.Headers, cancellationToken).ConfigureAwait(false),
                NullIfBlank(connection.UserAgent));
        }
        catch (InvalidOperationException ex)
        {
            rpcMetadata = new Metadata();
            metadataError = ex.Message;
        }

        if (metadataError is not null)
        {
            yield return ErrorEnvelope(metadataError);
            yield break;
        }

        var executor = new OperationExecutor(
            mappingResolver,
            session.Source,
            new GrpcTransport(channel),
            new JsonRequestTranslator(),
            new SelectionProjector(request.StrictSelection),
            new IntrospectionExecutor(new GraphQLSchemaBuilder(session.Source, mappingConfig), new SelectionProjector(request.StrictSelection)),
            new ExecutorOptions
            {
                RpcMetadata = rpcMetadata,
                Deadline = ParseDeadline(request.Deadline),
                EmitDefaults = request.EmitDefaults,
                AllowUnknownFields = request.AllowUnknownFields,
                RawOutput = request.Raw,
                IntrospectionEnabled = request.Introspection
            },
            new VerboseLogger(VerbosityLevel.Quiet));

        // Each streamed envelope is a synchronous WriteLine on the bridge's writer; a bounded channel turns
        // those lines into an async sequence and applies backpressure into the gRPC read loop (SPEC-030 §6).
        var envelopes = System.Threading.Channels.Channel.CreateBounded<string>(
            new System.Threading.Channels.BoundedChannelOptions(1000)
            {
                FullMode = System.Threading.Channels.BoundedChannelFullMode.Wait,
                SingleReader = true,
                SingleWriter = true
            });

        var writer = new ChannelTextWriter(envelopes.Writer, cancellationToken);
        var responseWriter = new StreamingResponseWriter(writer);

        var run = Task.Run(async () =>
        {
            try
            {
                await executor.StreamAsync(operation.OperationType, rootSelections, responseWriter, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                _ = envelopes.Writer.TryComplete();
            }
        }, cancellationToken);

        try
        {
            await foreach (var line in envelopes.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            {
                yield return line;
            }
        }
        finally
        {
            await run.ConfigureAwait(false); // observe completion / propagate cancellation
        }
    }

    public async Task<GraphQlSchemaResult> IntrospectAsync(GraphQlExecutionRequest request, CancellationToken cancellationToken)
    {
        var mappingConfig = await LoadMappingAsync(request, cancellationToken).ConfigureAwait(false);
        var connection = request.Connection;
        var (profile, password) = await ResolveTlsAsync(connection, cancellationToken).ConfigureAwait(false);
        var options = ConnectionChannelMapper.ToChannelOptions(connection, null, profile, password);
        var reflectionMetadata = ConnectionChannelMapper.BuildReflectionMetadata(connection);
        var (protosets, protos, imports) = ConnectionChannelMapper.DescriptorPaths(connection);

        var schemaName = string.IsNullOrWhiteSpace(mappingConfig.Defaults.Introspection.SchemaName)
            ? "Schema"
            : mappingConfig.Defaults.Introspection.SchemaName!;

        try
        {
            await using var session = await DescriptorSourceFactory.CreateAsync(
                connection.Address, protosets, protos, imports,
                channelOptions: options,
                reflectionMetadata: reflectionMetadata,
                cancellationToken: cancellationToken).ConfigureAwait(false);

            // GQL-075: __schema answered locally from the descriptor set — no business RPC.
            var schema = new GraphQLSchemaBuilder(session.Source, mappingConfig).BuildSchema();
            var types = MapSchemaTypes(schema);
            var json = schema.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true });

            return new GraphQlSchemaResult(Ok: true, schemaName, types, json, Error: null) { Sdl = BuildSdl(types) };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new GraphQlSchemaResult(Ok: false, schemaName, [], null,
                new GraphQlProblem(ex.Message, GraphQlProblemKind.Configuration));
        }
    }

    internal static IReadOnlyList<GraphQlSchemaType> MapSchemaTypes(JsonObject schema)
    {
        if (schema["types"] is not JsonArray types)
        {
            return [];
        }

        var result = new List<GraphQlSchemaType>();

        foreach (var node in types)
        {
            if (node is not JsonObject type || type["name"]?.GetValue<string>() is not { } name || name.StartsWith("__", StringComparison.Ordinal))
            {
                continue; // skip the introspection meta-types (__Type, __Schema, …)
            }

            var kind = type["kind"]?.GetValue<string>() ?? "OBJECT";
            var members = new List<GraphQlSchemaMember>();

            // Object / interface / input fields carry a type; enum values and union members are bare names.
            foreach (var key in (string[])["fields", "inputFields"])
            {
                if (type[key] is JsonArray fields)
                {
                    members.AddRange(fields.OfType<JsonObject>()
                        .Select(f => new GraphQlSchemaMember(f["name"]?.GetValue<string>() ?? "?", IntrospectionTypeName(f["type"]))));
                }
            }

            if (type["enumValues"] is JsonArray enumValues)
            {
                members.AddRange(enumValues.OfType<JsonObject>().Select(e => new GraphQlSchemaMember(e["name"]?.GetValue<string>() ?? "?", null)));
            }

            if (type["possibleTypes"] is JsonArray possibleTypes)
            {
                members.AddRange(possibleTypes.OfType<JsonObject>().Select(p => new GraphQlSchemaMember(p["name"]?.GetValue<string>() ?? "?", null)));
            }

            // The synthesiser stores the underlying proto FQN in the type's description (GQL-079 click-through).
            var description = type["description"]?.GetValue<string>();
            var symbol = description is not null && !description.Contains(' ', StringComparison.Ordinal) ? description : null;

            result.Add(new GraphQlSchemaType(name, kind, members) { Symbol = symbol });
        }

        return result;
    }

    /// <summary>GQL-078: render the derived type tree as SDL (labelled derived).</summary>
    internal static string BuildSdl(IReadOnlyList<GraphQlSchemaType> types)
    {
        var sb = new System.Text.StringBuilder();
        _ = sb.AppendLine("# Derived SDL — generated from the descriptors via introspection.");
        _ = sb.AppendLine();

        foreach (var type in types)
        {
            switch (type.Kind)
            {
                case "OBJECT" or "INTERFACE" or "INPUT_OBJECT":
                    var keyword = type.Kind switch { "INPUT_OBJECT" => "input", "INTERFACE" => "interface", _ => "type" };
                    _ = sb.AppendLine(Invariant($"{keyword} {type.Name} {{"));
                    foreach (var member in type.Members)
                    {
                        _ = sb.AppendLine(Invariant($"  {member.Name}: {member.TypeName ?? "String"}"));
                    }

                    _ = sb.AppendLine("}");
                    break;

                case "ENUM":
                    _ = sb.AppendLine(Invariant($"enum {type.Name} {{"));
                    foreach (var member in type.Members)
                    {
                        _ = sb.AppendLine(Invariant($"  {member.Name}"));
                    }

                    _ = sb.AppendLine("}");
                    break;

                case "UNION":
                    _ = sb.AppendLine(Invariant($"union {type.Name} = {string.Join(" | ", type.Members.Select(m => m.Name))}"));
                    break;

                case "SCALAR":
                    _ = sb.AppendLine(Invariant($"scalar {type.Name}"));
                    break;
            }

            _ = sb.AppendLine();
        }

        return sb.ToString().TrimEnd() + "\n";
    }

    /// <summary>Unwraps an introspection type ref (NON_NULL / LIST / ofType) into a display name like <c>[String!]</c>.</summary>
    private static string IntrospectionTypeName(JsonNode? type)
    {
        if (type is not JsonObject obj)
        {
            return "?";
        }

        return obj["kind"]?.GetValue<string>() switch
        {
            "NON_NULL" => IntrospectionTypeName(obj["ofType"]) + "!",
            "LIST" => "[" + IntrospectionTypeName(obj["ofType"]) + "]",
            _ => obj["name"]?.GetValue<string>() ?? "?"
        };
    }

    public async Task<GraphQlResolutionResult> ResolveAsync(GraphQlExecutionRequest request, CancellationToken cancellationToken)
    {
        GraphQLOperation operation;
        try
        {
            operation = GraphQLDocumentParser.Parse(request.Document).SelectOperation(request.OperationName);
        }
        catch (Exception)
        {
            return new GraphQlResolutionResult([], DefaultServiceOverridden: false, OverriddenService: null);
        }

        var mappingConfig = await LoadMappingAsync(request, cancellationToken).ConfigureAwait(false);
        var resolver = new MappingResolver(mappingConfig, request.DefaultService);
        var operationType = operation.OperationType;

        var fields = new List<GraphQlFieldResolution>();

        foreach (var field in operation.SelectionSet.Selections.OfType<GraphQLParser.AST.GraphQLField>())
        {
            var name = field.Name.StringValue;
            var hasExplicitEntry = mappingConfig.Operations.Any(e => e.GraphqlField == name && e.OperationType == operationType);

            try
            {
                var entry = resolver.Resolve(name, operationType);
                var kind = entry.Kind == MethodKind.ServerStreaming ? "serverStreaming" : "unary";
                var source = hasExplicitEntry ? GraphQlResolutionSource.ExplicitEntry : GraphQlResolutionSource.Convention;
                var derivation = source == GraphQlResolutionSource.Convention
                    ? $"{name} → {entry.Method} on {entry.Service}"
                    : null;

                fields.Add(new GraphQlFieldResolution(name, Resolved: true, entry.Service, entry.Method, kind, source, derivation, Error: null));
            }
            catch (InvalidOperationException ex)
            {
                fields.Add(new GraphQlFieldResolution(name, Resolved: false, null, null, null, GraphQlResolutionSource.Unresolved, null, ex.Message));
            }
        }

        // GQL-041: the tab's default-service overrides the mapping's defaults.service when both are set.
        var overridden = !string.IsNullOrWhiteSpace(request.DefaultService)
                         && !string.IsNullOrWhiteSpace(mappingConfig.Defaults.Service)
                         && request.DefaultService != mappingConfig.Defaults.Service;

        return new GraphQlResolutionResult(fields, overridden, overridden ? request.DefaultService : null);
    }

    /// <summary>Parse + select the operation + coerce variables (no network). Returns a problem on failure.</summary>
    private static (GraphQLOperation? Operation, IReadOnlyList<ResolvedSelection>? Selections, GraphQlProblem? Error) PrepareSelections(GraphQlExecutionRequest request)
    {
        GraphQLDocument document;
        GraphQLOperation operation;

        try
        {
            document = GraphQLDocumentParser.Parse(request.Document);
            operation = document.SelectOperation(request.OperationName);
        }
        catch (Exception ex)
        {
            return (null, null, ToSyntaxProblem(ex));
        }

        try
        {
            JsonNode? variablesFile = string.IsNullOrWhiteSpace(request.VariablesJson)
                ? null
                : VariableCoercer.ParseVariablesFile(request.VariablesJson);

            var variables = VariableCoercer.Coerce(operation.VariableDefinitions, cliVariables: null, variablesFile);
            var selections = new SelectionResolver(document.Fragments, variables).Resolve(operation.SelectionSet);
            return (operation, selections, null);
        }
        catch (ArgumentException ex)
        {
            return (null, null, new GraphQlProblem(ex.Message, GraphQlProblemKind.Variables));
        }
    }

    /// <summary>
    ///     Parses the envelope's <c>errors[]</c> into structured Studio models (GQL-070). Classification is by
    ///     kind (GQL-073): an entry carrying an upstream gRPC status is <see cref="GraphQlErrorClass.Upstream" />;
    ///     any other coded entry is treated as configuration/usage, not trusting <c>extensions.code</c>.
    /// </summary>
    internal static IReadOnlyList<GraphQlErrorInfo> ParseErrors(JsonObject envelope)
    {
        if (envelope["errors"] is not JsonArray errors)
        {
            return [];
        }

        var list = new List<GraphQlErrorInfo>(errors.Count);

        foreach (var node in errors)
        {
            if (node is not JsonObject error)
            {
                continue;
            }

            var message = error["message"]?.GetValue<string>() ?? "(error)";
            var path = error["path"] is JsonArray pathArray
                ? pathArray.Select(e => e?.ToString() ?? string.Empty).ToList()
                : (IReadOnlyList<string>)[];

            string? code = null;
            string? grpcStatus = null;
            int? grpcStatusCode = null;

            if (error["extensions"] is JsonObject extensions)
            {
                code = extensions["code"]?.GetValue<string>();
                grpcStatus = extensions["grpcStatus"]?.GetValue<string>();

                if (extensions["grpcStatusCode"] is JsonValue statusValue && statusValue.TryGetValue(out int parsed))
                {
                    grpcStatusCode = parsed;
                }
            }

            var category = grpcStatusCode is not null
                ? GraphQlErrorClass.Upstream
                : code is not null ? GraphQlErrorClass.Configuration : GraphQlErrorClass.Unknown;

            list.Add(new GraphQlErrorInfo(message, path, code, grpcStatus, grpcStatusCode, category));
        }

        return list;
    }

    private static string ErrorEnvelope(string message)
        => GraphQLResponseBuilder.BuildSingleError(new GraphQLError(message, [])).ToJsonString();

    /// <summary>A <see cref="TextWriter" /> that forwards each whole line to a channel, blocking when full (backpressure).</summary>
    private sealed class ChannelTextWriter(System.Threading.Channels.ChannelWriter<string> writer, CancellationToken cancellationToken) : TextWriter
    {
        public override System.Text.Encoding Encoding => System.Text.Encoding.UTF8;

        public override void WriteLine(string? value)
            => writer.WriteAsync(value ?? string.Empty, cancellationToken).AsTask().GetAwaiter().GetResult();
    }

    public async Task<GraphQlTranslationResult> TranslateAsync(GraphQlExecutionRequest request, CancellationToken cancellationToken)
    {
        var (operation, rootSelections, prepError) = PrepareSelections(request);

        if (prepError is not null || operation is null || rootSelections is null)
        {
            // Coercion / parse failures render in place of the JSON (GQL-050).
            return new GraphQlTranslationResult([new GraphQlFieldTranslation("(document)", null, null, [], prepError!.Message)]);
        }

        var mappingConfig = await LoadMappingAsync(request, cancellationToken).ConfigureAwait(false);
        var mappingResolver = new MappingResolver(mappingConfig, request.DefaultService);
        var translator = new JsonRequestTranslator();

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

        // GQL-047: index every argument's document position so a dropped argument can be squiggled.
        var argumentLocations = BuildArgumentLocations(operation, request.Document);

        var fields = new List<GraphQlFieldTranslation>();

        foreach (var selection in rootSelections)
        {
            fields.Add(await TranslateFieldAsync(selection, operation.OperationType, mappingResolver, mappingConfig, translator, session.Source, argumentLocations, cancellationToken).ConfigureAwait(false));
        }

        return new GraphQlTranslationResult(fields);
    }

    /// <summary>Maps each <c>(rootField, argument)</c> to its 1-based line/column in the document (GQL-047 squiggle).</summary>
    private static IReadOnlyDictionary<(string Field, string Argument), (int Line, int Column)> BuildArgumentLocations(GraphQLOperation operation, string document)
    {
        var map = new Dictionary<(string, string), (int, int)>();

        foreach (var field in operation.SelectionSet.Selections.OfType<GraphQLParser.AST.GraphQLField>())
        {
            foreach (var argument in field.Arguments?.Items ?? (IReadOnlyList<GraphQLParser.AST.GraphQLArgument>)[])
            {
                map[(field.Name.StringValue, argument.Name.StringValue)] = LineColumn(document, argument.Name.Location.Start);
            }
        }

        return map;
    }

    private static (int Line, int Column) LineColumn(string text, int offset)
    {
        int line = 1, column = 1;

        for (var i = 0; i < offset && i < text.Length; i++)
        {
            if (text[i] == '\n')
            {
                line++;
                column = 1;
            }
            else
            {
                column++;
            }
        }

        return (line, column);
    }

    private static async Task<GraphQlFieldTranslation> TranslateFieldAsync(
        ResolvedSelection selection,
        GraphQLOperationType operationType,
        MappingResolver mappingResolver,
        MappingConfig mappingConfig,
        JsonRequestTranslator translator,
        IDescriptorSource source,
        IReadOnlyDictionary<(string Field, string Argument), (int Line, int Column)> argumentLocations,
        CancellationToken cancellationToken)
    {
        try
        {
            var entry = mappingResolver.Resolve(selection.Name, operationType);
            var serviceName = entry.Service!;

            if (await source.FindSymbolAsync(serviceName, cancellationToken).ConfigureAwait(false) is not Google.Protobuf.Reflection.ServiceDescriptor service)
            {
                return new GraphQlFieldTranslation(selection.Name, $"{serviceName}/{entry.Method}", null, [], $"Service '{serviceName}' not found.");
            }

            var method = service.Methods.FirstOrDefault(m => string.Equals(m.Name, entry.Method, StringComparison.Ordinal));

            if (method is null)
            {
                return new GraphQlFieldTranslation(selection.Name, $"{serviceName}/{entry.Method}", null, [], $"Method '{serviceName}/{entry.Method}' not found.");
            }

            // The would-be request JSON (no descriptor → silent drop), per GQL-047's "request JSON showing no field".
            var requestJson = Prettify(translator.Translate(selection, entry, mappingConfig.Defaults, requestType: null));

            // The Finding-4 guard: re-translate WITH the descriptor; an unknown convention argument throws.
            var dropped = new List<GraphQlDroppedArgument>();
            try
            {
                _ = translator.Translate(selection, entry, mappingConfig.Defaults, method.InputType);
            }
            catch (Gql2Grpc.Translation.UnknownArgumentException unknown)
            {
                int? line = null, column = null;
                if (argumentLocations.TryGetValue((selection.Name, unknown.ArgumentName), out var location))
                {
                    line = location.Line;
                    column = location.Column;
                }

                dropped.Add(new GraphQlDroppedArgument(unknown.ArgumentName, line, column));
            }

            return new GraphQlFieldTranslation(selection.Name, $"{serviceName}/{entry.Method}", requestJson, dropped, null)
            {
                Annotations = Annotate(selection, entry, mappingConfig.Defaults),
                FieldMask = entry.SelectionFieldMaskPath is null ? null : NullIfEmpty(Gql2Grpc.Translation.FieldMaskProjector.Build(selection.Children))
            };
        }
        catch (InvalidOperationException ex)
        {
            return new GraphQlFieldTranslation(selection.Name, null, null, [], ex.Message);
        }
    }

    /// <summary>GQL-051: annotate how each top-level request field was produced (the rule kind + target).</summary>
    private static IReadOnlyList<GraphQlArgumentRule> Annotate(ResolvedSelection selection, MappingEntry entry, MappingDefaults defaults)
    {
        var annotations = new List<GraphQlArgumentRule>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var (argName, _) in selection.Arguments)
        {
            _ = seen.Add(argName);
            annotations.Add(ClassifyArgument(argName, entry, defaults));
        }

        // Literals are applied even when the caller didn't supply the argument.
        foreach (var (argName, rule) in entry.Arguments)
        {
            if (rule is ArgumentRule.Literal literal && seen.Add(argName))
            {
                annotations.Add(new GraphQlArgumentRule(argName, "literal", literal.Value));
            }
        }

        return annotations;
    }

    private static GraphQlArgumentRule ClassifyArgument(string argName, MappingEntry entry, MappingDefaults defaults)
    {
        if (entry.Arguments.TryGetValue(argName, out var rule))
        {
            return rule switch
            {
                ArgumentRule.Rename rename => new GraphQlArgumentRule(argName, "rename", rename.GrpcFieldName),
                ArgumentRule.PathRule { Path: "." } => new GraphQlArgumentRule(argName, "spread", null),
                ArgumentRule.PathRule path => new GraphQlArgumentRule(argName, "path", path.Path),
                ArgumentRule.Literal literal => new GraphQlArgumentRule(argName, "literal", literal.Value),
                ArgumentRule.SkipArgument => new GraphQlArgumentRule(argName, "skip", null),
                _ => new GraphQlArgumentRule(argName, "rule", null)
            };
        }

        var aliases = ConventionDefaults.MergeArgumentAliases(defaults.ArgumentAliases);

        return aliases.TryGetValue(argName, out var alias)
            ? new GraphQlArgumentRule(argName, "alias", alias)
            : new GraphQlArgumentRule(argName, "snake_case", ConventionDefaults.ToSnakeCase(argName));
    }

    private static string? NullIfEmpty(string value) => string.IsNullOrEmpty(value) ? null : value;

    private static string Prettify(string compactJson)
    {
        try
        {
            return JsonNode.Parse(compactJson)?.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true }) ?? compactJson;
        }
        catch (System.Text.Json.JsonException)
        {
            return compactJson;
        }
    }

    public async Task<IReadOnlyList<GraphQlProblem>> ValidateMappingSchemaAsync(GraphQlExecutionRequest request, CancellationToken cancellationToken)
    {
        MappingConfig config;
        try
        {
            config = await LoadMappingAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception)
        {
            return []; // the synchronous schema validation already reports load/parse errors
        }

        if (config.Operations.Count == 0)
        {
            return [];
        }

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

        return await ValidateEntriesAsync(config, session.Source, request.DefaultService, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>The descriptor-aware checks (GQL-046), separated from session setup so they can be tested offline.</summary>
    internal static async Task<IReadOnlyList<GraphQlProblem>> ValidateEntriesAsync(
        MappingConfig config, IDescriptorSource source, string? defaultService, CancellationToken cancellationToken)
    {
        var problems = new List<GraphQlProblem>();

        foreach (var entry in config.Operations)
        {
            var serviceName = entry.Service ?? config.Defaults.Service ?? defaultService;

            if (string.IsNullOrEmpty(serviceName))
            {
                problems.Add(Problem($"`{entry.GraphqlField}`: no service set (add the entry's service, defaults.service, or a default-service)."));
                continue;
            }

            if (await source.FindSymbolAsync(serviceName, cancellationToken).ConfigureAwait(false) is not Google.Protobuf.Reflection.ServiceDescriptor service)
            {
                var hint = Closest(await source.ListServicesAsync(cancellationToken).ConfigureAwait(false), serviceName);
                problems.Add(Problem($"`{entry.GraphqlField}`: service '{serviceName}' was not found{Did(hint)}."));
                continue;
            }

            var method = service.Methods.FirstOrDefault(m => string.Equals(m.Name, entry.Method, StringComparison.Ordinal));

            if (method is null)
            {
                var hint = Closest(service.Methods.Select(m => m.Name), entry.Method);
                problems.Add(Problem($"`{entry.GraphqlField}`: method '{serviceName}/{entry.Method}' was not found{Did(hint)}."));
                continue;
            }

            var actualKind = method.IsServerStreaming ? MethodKind.ServerStreaming : MethodKind.Unary;
            if (entry.Kind != actualKind)
            {
                problems.Add(Problem($"`{entry.GraphqlField}`: kind '{KindName(entry.Kind)}' does not match {serviceName}/{method.Name} ({KindName(actualKind)})."));
            }

            foreach (var (argName, rule) in entry.Arguments)
            {
                var path = rule switch
                {
                    ArgumentRule.PathRule p when p.Path != "." => p.Path,
                    ArgumentRule.Rename r => r.GrpcFieldName,
                    _ => null
                };

                if (path is not null)
                {
                    AddPathProblem(problems, entry.GraphqlField, $"argument `{argName}`", path, method.InputType);
                }
            }

            if (entry.SelectionFieldMaskPath is { } maskPath)
            {
                AddPathProblem(problems, entry.GraphqlField, "$selection fieldMask", maskPath, method.InputType);
            }

            if (entry.Response?.Unwrap is { } unwrap && method.OutputType.FindFieldByName(unwrap) is null)
            {
                var hint = Closest(method.OutputType.Fields.InDeclarationOrder().Select(f => f.Name), unwrap);
                problems.Add(Problem($"`{entry.GraphqlField}`: response.unwrap '{unwrap}' is not a field of {method.OutputType.Name}{Did(hint)}."));
            }
        }

        return problems;
    }

    private static void AddPathProblem(List<GraphQlProblem> problems, string field, string label, string path, Google.Protobuf.Reflection.MessageDescriptor inputType)
    {
        var current = inputType;
        var segments = path.Split('.');

        for (var i = 0; i < segments.Length; i++)
        {
            var f = current.FindFieldByName(segments[i]);

            if (f is null)
            {
                var hint = Closest(current.Fields.InDeclarationOrder().Select(x => x.Name), segments[i]);
                problems.Add(Problem($"`{field}`: {label} path '{path}' — '{segments[i]}' is not a field of {current.Name}{Did(hint)}."));
                return;
            }

            if (i < segments.Length - 1)
            {
                if (f.FieldType != Google.Protobuf.Reflection.FieldType.Message || f.MessageType is null)
                {
                    problems.Add(Problem($"`{field}`: {label} path '{path}' — '{segments[i]}' is not a message field, cannot descend into it."));
                    return;
                }

                current = f.MessageType;
            }
        }
    }

    private static GraphQlProblem Problem(string message) => new(message, GraphQlProblemKind.Configuration);

    private static string KindName(MethodKind kind) => kind == MethodKind.ServerStreaming ? "serverStreaming" : "unary";

    private static string Did(string? hint) => hint is null ? string.Empty : $" (did you mean '{hint}'?)";

    /// <summary>The closest candidate within a small edit distance, or null when none is close enough.</summary>
    internal static string? Closest(IEnumerable<string> candidates, string target)
    {
        string? best = null;
        var bestDistance = int.MaxValue;

        foreach (var candidate in candidates)
        {
            var distance = Levenshtein(candidate, target);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = candidate;
            }
        }

        return best is not null && bestDistance <= 3 ? best : null;
    }

    private static int Levenshtein(string a, string b)
    {
        var d = new int[a.Length + 1, b.Length + 1];

        for (var i = 0; i <= a.Length; i++)
        {
            d[i, 0] = i;
        }

        for (var j = 0; j <= b.Length; j++)
        {
            d[0, j] = j;
        }

        for (var i = 1; i <= a.Length; i++)
        {
            for (var j = 1; j <= b.Length; j++)
            {
                var cost = a[i - 1] == b[j - 1] ? 0 : 1;
                d[i, j] = Math.Min(Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1), d[i - 1, j - 1] + cost);
            }
        }

        return d[a.Length, b.Length];
    }

    public IReadOnlyList<GraphQlProblem> ValidateMapping(string mappingText)
    {
        try
        {
            _ = MappingConfigLoader.FromText(mappingText);
            return [];
        }
        catch (Exception ex)
        {
            // YAML/JSON parse, missing keys, duplicate entries, unknown enums — all mapping-content problems.
            return [new GraphQlProblem(ex.Message, GraphQlProblemKind.Configuration)];
        }
    }

    // GQL-044: the inline mapping buffer takes precedence over the external file path.
    private static Task<MappingConfig> LoadMappingAsync(GraphQlExecutionRequest request, CancellationToken cancellationToken)
        => string.IsNullOrWhiteSpace(request.MappingText)
            ? MappingConfigLoader.LoadAsync(request.MappingPath, cancellationToken)
            : Task.FromResult(MappingConfigLoader.FromText(request.MappingText));

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

    private static int RootFieldCount(GraphQLOperation op)
        => op.SelectionSet.Selections.Count(s => s is GraphQLParser.AST.GraphQLField);

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

    private static VerbosityLevel ToVerbosity(GraphQlVerbosity verbosity) => verbosity switch
    {
        GraphQlVerbosity.VeryVerbose => VerbosityLevel.VeryVerbose,
        _ => VerbosityLevel.Verbose
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
