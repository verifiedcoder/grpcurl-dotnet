using Google.Protobuf.Reflection;
using Gql2Grpc.Configuration;
using Gql2Grpc.Diagnostics;
using Gql2Grpc.GraphQL;
using Gql2Grpc.Introspection;
using Gql2Grpc.Response;
using Gql2Grpc.Translation;
using GrpCurl.Net.DescriptorSources;
using System.Text.Json.Nodes;

namespace Gql2Grpc.Execution;

internal sealed class OperationExecutor(
    MappingResolver mappingResolver,
    IDescriptorSource descriptorSource,
    GrpcTransport transport,
    IRequestTranslator translator,
    SelectionProjector projector,
    IntrospectionExecutor introspection,
    ExecutorOptions options,
    VerboseLogger logger)
{
    public async Task<JsonObject> ExecuteUnaryAsync(
        GraphQLOperationType operationType,
        IReadOnlyList<ResolvedSelection> rootSelections,
        CancellationToken cancellationToken,
        IProgress<FieldExecutionProgress>? progress = null)
    {
        if (rootSelections.Count == 0)
        {
            return GraphQLResponseBuilder.Build([], []);
        }

        var results = await ParallelFieldScheduler.RunAsync(
            rootSelections,
            RunOne,
            cancellationToken,
            progress,
            static selection => selection.ResponseKey).ConfigureAwait(false);

        return GraphQLResponseBuilder.Build(results, []);

        async Task<RootFieldResult> RunOne(ResolvedSelection selection, CancellationToken ct)
        {
            if (options.IntrospectionEnabled && IntrospectionExecutor.IsIntrospectionField(selection.Name))
            {
                return introspection.Execute(selection, operationType);
            }

            return await ExecuteFieldUnaryAsync(selection, operationType, ct).ConfigureAwait(false);
        }
    }

    public async Task StreamAsync(
        GraphQLOperationType operationType,
        IReadOnlyList<ResolvedSelection> rootSelections,
        StreamingResponseWriter writer,
        CancellationToken cancellationToken)
    {
        if (rootSelections.Count != 1)
        {
            writer.WriteError(new GraphQLError(
                                  $"Subscription operations must contain exactly one root field (got {rootSelections.Count}).",
                                  []));

            return;
        }

        var selection = rootSelections[0];

        if (options.IntrospectionEnabled && IntrospectionExecutor.IsIntrospectionField(selection.Name))
        {
            var introResult = introspection.Execute(selection, operationType);

            if (introResult.Failed)
            {
                foreach (var err in introResult.Errors)
                {
                    writer.WriteError(err);
                }

                return;
            }

            writer.WriteData(selection.ResponseKey, introResult.Data);

            return;
        }

        await ExecuteFieldStreamingAsync(selection, operationType, writer, cancellationToken).ConfigureAwait(false);
    }

    private async Task<RootFieldResult> ExecuteFieldUnaryAsync(
        ResolvedSelection selection,
        GraphQLOperationType operationType,
        CancellationToken cancellationToken)
    {
        var errors = new List<GraphQLError>();

        try
        {
            var prepared = await PrepareFieldAsync(selection, operationType, cancellationToken).ConfigureAwait(false);

            if (prepared.Entry.Kind == MethodKind.ServerStreaming)
            {
                errors.Add(new GraphQLError(
                               "Server-streaming methods cannot be used as a query or mutation field.",
                               [selection.ResponseKey]));

                return new RootFieldResult(selection.ResponseKey, null, errors, true);
            }

            if (options.RawOutput)
            {
                var raw = await transport.InvokeUnaryAsync(
                    prepared.Method, prepared.RequestJson, options.RpcMetadata, options.Deadline,
                    options.EmitDefaults, options.AllowUnknownFields, cancellationToken).ConfigureAwait(false);

                return new RootFieldResult(selection.ResponseKey, JsonNode.Parse(raw), errors, false);
            }

            var responseJson = await transport.InvokeUnaryAsync(
                prepared.Method, prepared.RequestJson, options.RpcMetadata, options.Deadline,
                options.EmitDefaults, options.AllowUnknownFields, cancellationToken).ConfigureAwait(false);

            var source = JsonNode.Parse(responseJson);
            var projected = projector.Project(source, selection.Children, prepared.Entry.Response, [selection.ResponseKey], errors);

            return new RootFieldResult(selection.ResponseKey, projected, errors, false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            var error = ExceptionTranslator.ToFieldError(ex, selection.ResponseKey);

            errors.Add(error);

            return new RootFieldResult(selection.ResponseKey, null, errors, true);
        }
    }

    private async Task ExecuteFieldStreamingAsync(
        ResolvedSelection selection,
        GraphQLOperationType operationType,
        StreamingResponseWriter writer,
        CancellationToken cancellationToken)
    {
        try
        {
            var prepared = await PrepareFieldAsync(selection, operationType, cancellationToken).ConfigureAwait(false);

            if (prepared.Entry.Kind != MethodKind.ServerStreaming)
            {
                writer.WriteError(new GraphQLError(
                                      "Subscription operations require a server-streaming gRPC method.",
                                      [selection.ResponseKey]));

                return;
            }

            await foreach (var messageJson in transport.InvokeServerStreamingAsync(
                               prepared.Method, prepared.RequestJson, options.RpcMetadata, options.Deadline,
                               options.EmitDefaults, options.AllowUnknownFields, cancellationToken).ConfigureAwait(false))
            {
                var source = JsonNode.Parse(messageJson);
                var projectorErrors = new List<GraphQLError>();
                var projected = options.RawOutput
                    ? source
                    : projector.Project(source, selection.Children, prepared.Entry.Response, [selection.ResponseKey], projectorErrors);

                foreach (var err in projectorErrors)
                {
                    writer.WriteError(err);
                }

                writer.WriteData(selection.ResponseKey, projected);
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            writer.WriteError(ExceptionTranslator.ToFieldError(ex, selection.ResponseKey));
        }
    }

    private async Task<PreparedField> PrepareFieldAsync(
        ResolvedSelection selection,
        GraphQLOperationType operationType,
        CancellationToken cancellationToken)
    {
        var entry = mappingResolver.Resolve(selection.Name, operationType);
        var serviceName = entry.Service ?? throw new InvalidOperationException($"Mapping for '{selection.Name}' has no service after resolution.");

        var symbol = await descriptorSource.FindSymbolAsync(serviceName, cancellationToken).ConfigureAwait(false);

        if (symbol is not ServiceDescriptor svc)
        {
            throw new InvalidOperationException($"Service '{serviceName}' not found.");
        }

        var method = svc.Methods.FirstOrDefault(m => string.Equals(m.Name, entry.Method, StringComparison.Ordinal))
                     ?? throw new InvalidOperationException($"Method '{serviceName}.{entry.Method}' not found.");

        var requestJson = translator.Translate(selection, entry, mappingResolver.Config.Defaults, method.InputType);

        logger.Verbose($"[{selection.ResponseKey}] → {serviceName}/{entry.Method}");
        logger.VeryVerbose($"[{selection.ResponseKey}] request JSON: {requestJson}");

        return new PreparedField(entry, method, requestJson);
    }

    private readonly record struct PreparedField(MappingEntry Entry, MethodDescriptor Method, string RequestJson);
}