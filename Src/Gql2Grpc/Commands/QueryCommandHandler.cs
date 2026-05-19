using Gql2Grpc.Configuration;
using Gql2Grpc.Diagnostics;
using Gql2Grpc.Execution;
using Gql2Grpc.GraphQL;
using Gql2Grpc.Introspection;
using Gql2Grpc.Response;
using Gql2Grpc.Translation;
using GrpCurl.Net.DescriptorSources;
using GrpCurl.Net.Exceptions;
using GrpCurl.Net.Utilities;
using System.CommandLine;
using System.Text.Json.Nodes;

namespace Gql2Grpc.Commands;

internal static class QueryCommandHandler
{
    public static async Task<int> InvokeAsync(string[] args)
    {
        var command = Create();
        var parse = command.Parse(args);
        return await parse.InvokeAsync().ConfigureAwait(false);
    }

    public static RootCommand Create()
    {
        var addressArg = new Argument<string>("address")
        {
            Description = "gRPC server address (host:port)"
        };

        var queryArg = new Argument<string?>("query")
        {
            Description = "GraphQL document (or use -f to read from file)",
            Arity = ArgumentArity.ZeroOrOne
        };

        var protosetOpt = new Option<string[]>("--protoset")
        {
            Description = "Protoset file(s). If omitted, reflection is used.",
            Arity = ArgumentArity.ZeroOrMore
        };

        var protosetOutOpt = new Option<string?>("--protoset-out")
        {
            Description = "Write discovered FileDescriptorSet to a file."
        };

        var forceOpt = new Option<bool>("--force")
        {
            Description = "Overwrite existing files (e.g., target of --protoset-out) without confirmation."
        };

        var plaintextOpt = new Option<bool>("--plaintext") { Description = "Use plaintext HTTP/2" };
        var insecureOpt = new Option<bool>("--insecure") { Description = "Skip TLS certificate verification" };
        var cacertOpt = new Option<string?>("--cacert") { Description = "CA certificate file path" };
        var certOpt = new Option<string?>("--cert") { Description = "Client certificate file path (mTLS)" };
        var keyOpt = new Option<string?>("--key") { Description = "Client private key file path (mTLS PEM)" };
        var certPasswordOpt = new Option<string?>("--cert-password") { Description = "Password for PKCS12 client certificate" };
        var authorityOpt = new Option<string?>("--authority") { Description = ":authority header / TLS server name" };
        var serverNameOpt = new Option<string?>("--servername") { Description = "Override TLS server name" };
        var userAgentOpt = new Option<string?>("--user-agent")
        {
            Description = $"Custom User-Agent header (default: {UserAgentProvider.Default})"
        };

        var connectTimeoutOpt = new Option<string?>("--connect-timeout") { Description = "Connection timeout (e.g. '10s')" };
        var maxTimeOpt = new Option<string?>("--max-time") { Description = "Overall deadline (e.g. '30s')" };
        var maxMsgSzOpt = new Option<string?>("--max-msg-sz") { Description = "Maximum message size (e.g. '4MB')" };

        var headerOpt = new Option<string[]>("--header", "-H")
        {
            Description = "Header (name: value); applied to both reflection and RPC",
            Arity = ArgumentArity.ZeroOrMore
        };

        var reflectHeaderOpt = new Option<string[]>("--reflect-header")
        {
            Description = "Header sent only on reflection requests",
            Arity = ArgumentArity.ZeroOrMore
        };

        var rpcHeaderOpt = new Option<string[]>("--rpc-header")
        {
            Description = "Header sent only on RPC requests",
            Arity = ArgumentArity.ZeroOrMore
        };

        var fileOpt = new Option<string?>("--file", "-f") { Description = "Read GraphQL document from file" };
        var operationOpt = new Option<string?>("--operation") { Description = "Operation name to execute from a multi-op document" };
        var varOpt = new Option<string[]>("--var")
        {
            Description = "Operation variable (name=value)",
            Arity = ArgumentArity.ZeroOrMore
        };

        var variablesFileOpt = new Option<string?>("--variables-file") { Description = "JSON file of operation variables" };

        var mappingOpt = new Option<string?>("--mapping") { Description = "Mapping file (YAML or JSON)" };
        var defaultServiceOpt = new Option<string?>("--default-service") { Description = "Fully-qualified gRPC service used by convention fallback" };

        var emitDefaultsOpt = new Option<bool>("--emit-defaults") { Description = "Include default values in protobuf JSON output" };
        var allowUnknownFieldsOpt = new Option<bool>("--allow-unknown-fields")
        {
            Description = "Skip unknown fields in request JSON instead of erroring",
            DefaultValueFactory = _ => true
        };

        var strictSelectionOpt = new Option<bool>("--strict-selection") { Description = "Missing response fields raise GraphQL errors instead of null" };
        var rawOpt = new Option<bool>("--raw") { Description = "Emit unshaped gRPC JSON only (bypass selection projection)" };

        var introspectionOpt = new Option<bool>("--introspection")
        {
            Description = "Enable __schema/__type/__typename interception",
            DefaultValueFactory = _ => true
        };

        var verboseOpt = new Option<bool>("--verbose", "-v") { Description = "Verbose diagnostics on stderr" };
        var veryVerboseOpt = new Option<bool>("--very-verbose", "--vv") { Description = "Very verbose diagnostics with request/response JSON" };

        var command = new RootCommand(CommandDescriptions.Root)
        {
            addressArg, queryArg,
            protosetOpt,
            protosetOutOpt,
            forceOpt,
            plaintextOpt, insecureOpt, cacertOpt, certOpt, keyOpt, certPasswordOpt,
            authorityOpt, serverNameOpt, userAgentOpt,
            connectTimeoutOpt, maxTimeOpt, maxMsgSzOpt,
            headerOpt, reflectHeaderOpt, rpcHeaderOpt,
            fileOpt, operationOpt, varOpt, variablesFileOpt,
            mappingOpt, defaultServiceOpt,
            emitDefaultsOpt, allowUnknownFieldsOpt, strictSelectionOpt, rawOpt,
            introspectionOpt,
            verboseOpt, veryVerboseOpt
        };

        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var cliArgs = new CliOptions
            {
                Address = parseResult.GetValue(addressArg)!,
                QueryInline = parseResult.GetValue(queryArg),
                QueryFile = parseResult.GetValue(fileOpt),
                OperationName = parseResult.GetValue(operationOpt),
                Variables = parseResult.GetValue(varOpt) ?? [],
                VariablesFile = parseResult.GetValue(variablesFileOpt),
                Protosets = parseResult.GetValue(protosetOpt) ?? [],
                ProtosetOut = parseResult.GetValue(protosetOutOpt),
                Force = parseResult.GetValue(forceOpt),
                Plaintext = parseResult.GetValue(plaintextOpt),
                Insecure = parseResult.GetValue(insecureOpt),
                CaCert = parseResult.GetValue(cacertOpt),
                Cert = parseResult.GetValue(certOpt),
                Key = parseResult.GetValue(keyOpt),
                CertPassword = parseResult.GetValue(certPasswordOpt),
                Authority = parseResult.GetValue(authorityOpt),
                ServerName = parseResult.GetValue(serverNameOpt),
                UserAgent = parseResult.GetValue(userAgentOpt),
                ConnectTimeout = parseResult.GetValue(connectTimeoutOpt),
                MaxTime = parseResult.GetValue(maxTimeOpt),
                MaxMessageSize = parseResult.GetValue(maxMsgSzOpt),
                Headers = parseResult.GetValue(headerOpt) ?? [],
                ReflectHeaders = parseResult.GetValue(reflectHeaderOpt) ?? [],
                RpcHeaders = parseResult.GetValue(rpcHeaderOpt) ?? [],
                MappingPath = parseResult.GetValue(mappingOpt),
                DefaultService = parseResult.GetValue(defaultServiceOpt),
                EmitDefaults = parseResult.GetValue(emitDefaultsOpt),
                AllowUnknownFields = parseResult.GetValue(allowUnknownFieldsOpt),
                StrictSelection = parseResult.GetValue(strictSelectionOpt),
                Raw = parseResult.GetValue(rawOpt),
                Introspection = parseResult.GetValue(introspectionOpt),
                Verbose = parseResult.GetValue(verboseOpt),
                VeryVerbose = parseResult.GetValue(veryVerboseOpt)
            };

            try
            {
                return await ExecuteAsync(cliArgs, cancellationToken).ConfigureAwait(false);
            }
            catch (GrpcCommandException ex)
            {
                if (!ex.Silent)
                {
                    EmitTopLevelError(new GraphQLError(
                        ex.Message,
                        [],
                        new Dictionary<string, object?> { ["code"] = "USAGE" }));
                }

                return ex.ExitCode;
            }
            catch (Exception ex)
            {
                EmitTopLevelError(ExceptionTranslator.ToTopLevelError(ex));

                return ExceptionTranslator.ExitCodeFor(ex);
            }
        });

        return command;
    }

    private static async Task<int> ExecuteAsync(CliOptions cli, CancellationToken cancellationToken)
    {
        var verbosity = GetVerbosity(cli);
        var logger = new VerboseLogger(verbosity);

        // --max-time bounds the *entire* GraphQL-to-gRPC operation, including query/variables
        // file reads, mapping load, descriptor source resolution, and the actual gRPC call.
        // The earlier implementation only enforced it on the gRPC deadline, so slow file
        // reads or descriptor probes could outlive the budget.
        TimeSpan? maxTimeSpan = null;

        if (cli.MaxTime is not null)
        {
            maxTimeSpan = GrpcChannelFactory.ParseDuration(cli.MaxTime);
        }

        using var deadlineCts = maxTimeSpan is not null
            ? new CancellationTokenSource(maxTimeSpan.Value)
            : new CancellationTokenSource();

        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(deadlineCts.Token, cancellationToken);

        var operationToken = linkedCts.Token;

        var queryText = await ResolveQueryAsync(cli, operationToken).ConfigureAwait(false);
        var document = GraphQLDocumentParser.Parse(queryText);
        var operation = document.SelectOperation(cli.OperationName);

        var variablesFile = cli.VariablesFile is null
            ? null
            : VariableCoercer.ParseVariablesFile(await File.ReadAllTextAsync(cli.VariablesFile, operationToken).ConfigureAwait(false));

        var cliVars = ParseCliVariables(cli.Variables);
        var coercedVariables = VariableCoercer.Coerce(operation.VariableDefinitions, cliVars, variablesFile);

        var resolver = new SelectionResolver(document.Fragments, coercedVariables);
        var rootSelections = resolver.Resolve(operation.SelectionSet);

        var mappingConfig = await MappingConfigLoader.LoadAsync(cli.MappingPath, operationToken).ConfigureAwait(false);
        var mappingResolver = new MappingResolver(mappingConfig, cli.DefaultService);

        var channelOptions = BuildChannelOptions(cli);
        var reflectHeaders = cli.Headers.Concat(cli.ReflectHeaders);
        var rpcHeaders = cli.Headers.Concat(cli.RpcHeaders);

        var reflectionMetadata = GrpcChannelFactory.CreateMetadata(reflectHeaders, cli.UserAgent);
        var rpcMetadata = GrpcChannelFactory.CreateMetadata(rpcHeaders, cli.UserAgent);

        await using var descriptorBundle = await DescriptorSourceFactory.CreateAsync(
            cli.Address, cli.Protosets, channelOptions, reflectionMetadata, operationToken).ConfigureAwait(false);

        /*
         * Gql2Grpc always invokes RPCs against a live server even when --protoset is used
         * for offline schema: the GraphQL query still has to be forwarded. Reject the
         * protoset-only-no-address combination here with a clear message.
         */
        var transportChannel = descriptorBundle.Channel ?? throw new GrpcCommandException(
            "Gql2Grpc requires a target gRPC address; supply <address> or use --address.",
            exitCode: 2);

        var transport = new GrpcTransport(transportChannel);
        var translator = new JsonRequestTranslator();
        var projector = new SelectionProjector(cli.StrictSelection);
        var schemaBuilder = new GraphQLSchemaBuilder(descriptorBundle.Source, mappingConfig);
        var introspection = new IntrospectionExecutor(schemaBuilder, projector);

        var deadline = maxTimeSpan is null
            ? (DateTime?)null
            : DateTime.UtcNow + maxTimeSpan.Value;

        var executorOptions = new ExecutorOptions
        {
            RpcMetadata = rpcMetadata,
            Deadline = deadline,
            EmitDefaults = cli.EmitDefaults,
            AllowUnknownFields = cli.AllowUnknownFields,
            RawOutput = cli.Raw,
            IntrospectionEnabled = cli.Introspection
        };

        var executor = new OperationExecutor(
            mappingResolver, descriptorBundle.Source, transport, translator, projector,
            introspection, executorOptions, logger);

        int exitCode;

        if (operation.OperationType == GraphQLOperationType.Subscription || AnyFieldIsStreaming(rootSelections, operation.OperationType, mappingResolver))
        {
            var writer = new StreamingResponseWriter(Console.Out);
            await executor.StreamAsync(operation.OperationType, rootSelections, writer, operationToken).ConfigureAwait(false);
            exitCode = 0;
        }
        else
        {
            var envelope = await executor.ExecuteUnaryAsync(operation.OperationType, rootSelections, operationToken).ConfigureAwait(false);
            Console.WriteLine(GraphQLResponseBuilder.Serialize(envelope));
            exitCode = ExitCodeFromEnvelope(envelope);
        }

        if (string.IsNullOrEmpty(cli.ProtosetOut))
        {
            return exitCode;
        }

        await ProtosetExporter.WriteProtosetAsync(
            descriptorBundle.Source,
            cli.ProtosetOut,
            cli.Force, []).ConfigureAwait(false);

        logger.Verbose($"Wrote FileDescriptorSet to {cli.ProtosetOut}");

        return exitCode;
    }

    private static void EmitTopLevelError(GraphQLError error)
    {
        var envelope = GraphQLResponseBuilder.BuildSingleError(error);

        Console.WriteLine(GraphQLResponseBuilder.Serialize(envelope));
    }

    private static VerbosityLevel GetVerbosity(CliOptions cli)
    {
        if (cli.VeryVerbose)
        {
            return VerbosityLevel.VeryVerbose;
        }

        return cli.Verbose ? VerbosityLevel.Verbose : VerbosityLevel.Quiet;
    }

    private static int ExitCodeFromEnvelope(JsonObject envelope)
    {
        if (envelope["errors"] is not JsonArray errors || errors.Count == 0)
        {
            return 0;
        }

        foreach (var error in errors)
        {
            if (error is JsonObject errObj
                && errObj["extensions"] is JsonObject ext
                && ext["grpcStatusCode"] is JsonValue codeValue
                && codeValue.TryGetValue(out int grpcStatusCode))
            {
                return 64 + grpcStatusCode;
            }
        }

        return 1;
    }

    private static bool AnyFieldIsStreaming(
        IReadOnlyList<ResolvedSelection> rootSelections,
        GraphQLOperationType operationType,
        MappingResolver mappingResolver)
    {
        return rootSelections
            .Where(selection => !IntrospectionExecutor.IsIntrospectionField(selection.Name))
            .Select(selection => TryResolveMapping(selection.Name, operationType, mappingResolver))
            .Any(entry => entry?.Kind == MethodKind.ServerStreaming);
    }

    private static MappingEntry? TryResolveMapping(
        string selectionName,
        GraphQLOperationType operationType,
        MappingResolver mappingResolver)
    {
        try
        {
            return mappingResolver.Resolve(selectionName, operationType);
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    private static async Task<string> ResolveQueryAsync(CliOptions cli, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrEmpty(cli.QueryFile))
        {
            return await File.ReadAllTextAsync(cli.QueryFile, cancellationToken).ConfigureAwait(false);
        }

        return !string.IsNullOrEmpty(cli.QueryInline)
            ? cli.QueryInline!
            : throw new GrpcCommandException("No GraphQL document supplied. Pass a positional query string or --file.", exitCode: 2);
    }

    private static Dictionary<string, string> ParseCliVariables(IReadOnlyList<string> variables)
    {
        var dict = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var entry in variables)
        {
            if (string.IsNullOrWhiteSpace(entry))
            {
                continue;
            }

            var equals = entry.IndexOf('=');

            if (equals < 0)
            {
                throw new GrpcCommandException($"Variable '{entry}' must be in 'name=value' form.", exitCode: 2);
            }

            var name = entry[..equals].Trim();
            var value = entry[(equals + 1)..];

            dict[name] = value;
        }

        return dict;
    }

    private static GrpcChannelFactory.ChannelOptions BuildChannelOptions(CliOptions cli)
    {
        TimeSpan? connectTimeout = cli.ConnectTimeout is null ? null : GrpcChannelFactory.ParseDuration(cli.ConnectTimeout);

        int? maxMsgSize = cli.MaxMessageSize is null ? null : GrpcChannelFactory.ParseSize(cli.MaxMessageSize);

        return new GrpcChannelFactory.ChannelOptions
        {
            Plaintext = cli.Plaintext,
            InsecureSkipVerify = cli.Insecure,
            CaCertPath = cli.CaCert,
            ClientCertPath = cli.Cert,
            ClientKeyPath = cli.Key,
            ClientCertPassword = cli.CertPassword,
            ConnectTimeout = connectTimeout,
            MaxReceiveMessageSize = maxMsgSize,
            MaxSendMessageSize = maxMsgSize,
            Authority = cli.Authority,
            ServerName = cli.ServerName
        };
    }

    private sealed record CliOptions
    {
        public required string Address { get; init; }
        public string? QueryInline { get; init; }

        public string? QueryFile { get; init; }

        public string? OperationName { get; init; }

        public IReadOnlyList<string> Variables { get; init; } = [];

        public string? VariablesFile { get; init; }

        public IReadOnlyList<string> Protosets { get; init; } = [];

        public string? ProtosetOut { get; init; }

        public bool Force { get; init; }

        public bool Plaintext { get; init; }

        public bool Insecure { get; init; }

        public string? CaCert { get; init; }

        public string? Cert { get; init; }

        public string? Key { get; init; }

        public string? CertPassword { get; init; }

        public string? Authority { get; init; }

        public string? ServerName { get; init; }

        public string? UserAgent { get; init; }

        public string? ConnectTimeout { get; init; }

        public string? MaxTime { get; init; }

        public string? MaxMessageSize { get; init; }

        public IReadOnlyList<string> Headers { get; init; } = [];

        public IReadOnlyList<string> ReflectHeaders { get; init; } = [];

        public IReadOnlyList<string> RpcHeaders { get; init; } = [];

        public string? MappingPath { get; init; }

        public string? DefaultService { get; init; }

        public bool EmitDefaults { get; init; }

        public bool AllowUnknownFields { get; init; } = true;

        public bool StrictSelection { get; init; }

        public bool Raw { get; init; }

        public bool Introspection { get; init; } = true;

        public bool Verbose { get; init; }

        public bool VeryVerbose { get; init; }
    }
}
