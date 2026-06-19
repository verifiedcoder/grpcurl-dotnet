using Google.Protobuf.Reflection;
using Grpc.Core;
using GrpCurl.Net.DescriptorSources;
using GrpCurl.Net.Exceptions;
using GrpCurl.Net.Invocation;
using GrpCurl.Net.Output;
using GrpCurl.Net.Utilities;
using Spectre.Console;
using System.CommandLine;

namespace GrpCurl.Net.Commands;

internal static class DescribeCommandHandler
{
    public static Command Create()
    {
        var addressArg = new Argument<string?>("address")
        {
            Description = "Server address",
            Arity = ArgumentArity.ZeroOrOne
        };

        var symbolArg = new Argument<string?>("symbol")
        {
            Description = "Symbol to describe",
            Arity = ArgumentArity.ZeroOrOne
        };

        var protosetOpt = new Option<string[]>("--protoset")
        {
            Description = "Protoset file(s)",
            Arity = ArgumentArity.ZeroOrMore
        };

        var plaintextOpt = new Option<bool>("--plaintext")
        {
            Description = "Use plaintext HTTP/2"
        };

        var insecureOpt = new Option<bool>("--insecure")
        {
            Description = "Skip cert verification"
        };

        var cacertOpt = new Option<string?>("--cacert")
        {
            Description = "CA certificate file path for server certificate validation"
        };

        var certOpt = new Option<string?>("--cert")
        {
            Description = "Client certificate file path for mutual TLS"
        };

        var keyOpt = new Option<string?>("--key")
        {
            Description = "Client private key file path for mutual TLS"
        };

        var certPasswordOpt = new Option<string?>("--cert-password")
        {
            Description = "Password for PKCS12 (.p12/.pfx) client certificate"
        };

        var connectTimeoutOpt = new Option<string?>("--connect-timeout")
        {
            Description = "Connection timeout (e.g., '10s', '1m', '500ms'). Default: 10s"
        };

        var maxTimeOpt = new Option<string?>("--max-time")
        {
            Description = "Maximum time for the whole describe operation (e.g., '30s', '5m')."
        };

        var authorityOpt = new Option<string?>("--authority")
        {
            Description = "Value to use for :authority header and TLS server name"
        };

        var serverNameOpt = new Option<string?>("--servername")
        {
            Description = "Override TLS server name for certificate validation"
        };

        var verboseOpt = new Option<bool>("--verbose", "-v")
        {
            Description = "Verbose output"
        };

        var veryVerboseOpt = new Option<bool>("--very-verbose", "--vv")
        {
            Description = "Very verbose output with detailed timing information"
        };

        var userAgentOpt = new Option<string?>("--user-agent")
        {
            Description = "User-Agent header value. Default: grpcn/1.0.0"
        };

        var headerOpt = new Option<string[]>("--header", "-H")
        {
            Description = "Headers for reflection requests (name: value)",
            Arity = ArgumentArity.ZeroOrMore
        };

        var reflectHeaderOpt = new Option<string[]>("--reflect-header")
        {
            Description = "Headers for reflection requests only (name: value)",
            Arity = ArgumentArity.ZeroOrMore
        };

        var msgTemplateOpt = new Option<bool>("--msg-template")
        {
            Description = "Output a JSON template for the message type"
        };

        var protosetOutOpt = new Option<string?>("--protoset-out")
        {
            Description = "Write FileDescriptorSet to file after operation"
        };

        var outputOpt = OutputFormatOption.Build();

        var forceOpt = new Option<bool>("--force")
        {
            Description = "Overwrite existing files (e.g., target of --protoset-out) without confirmation"
        };

        var revocationModeOpt = new Option<string?>("--revocation-mode")
        {
            Description = "Certificate revocation policy when --cacert is used: " +
                          "online (default, fetches CRL/OCSP), offline (uses cached CRL only), " +
                          "nocheck (disables revocation — only safe for self-signed test fixtures)."
        };

        var exportableKeyOpt = new Option<bool>("--exportable-key")
        {
            Description = "Load client private keys with X509KeyStorageFlags.Exportable. " +
                          "Default is EphemeralKeySet on Linux, platform default on macOS, " +
                          "and non-exportable UserKeySet on Windows."
        };

        var keepaliveTimeOpt = new Option<string?>("--keepalive-time")
        {
            Description = "HTTP/2 keepalive ping interval (e.g., '30s'). Default: 60s."
        };

        var keepaliveTimeoutOpt = new Option<string?>("--keepalive-timeout")
        {
            Description = "HTTP/2 keepalive ping timeout (e.g., '10s'). Default: 30s."
        };

        var protoOpt = new Option<string[]>("--proto")
        {
            Description = "Path to a .proto source file to compile via protoc. Repeatable. " +
                          "Requires protoc on PATH (alternative: --protoset).",
            Arity = ArgumentArity.ZeroOrMore
        };

        var importPathOpt = new Option<string[]>("--import-path", "-I")
        {
            Description = "Directory passed to protoc as an import root. Repeatable.",
            Arity = ArgumentArity.ZeroOrMore
        };

        var protoOutDirOpt = new Option<string?>("--proto-out-dir")
        {
            Description = "Reconstruct .proto source files from the active schema and write them to this directory. " +
                          "Refuses to overwrite without --force."
        };

        var command = new Command("describe", CommandDescriptions.Describe)
        {
            addressArg,
            symbolArg,
            protosetOpt,
            plaintextOpt,
            insecureOpt,
            cacertOpt,
            certOpt,
            keyOpt,
            certPasswordOpt,
            connectTimeoutOpt,
            maxTimeOpt,
            authorityOpt,
            serverNameOpt,
            verboseOpt,
            veryVerboseOpt,
            userAgentOpt,
            headerOpt,
            reflectHeaderOpt,
            msgTemplateOpt,
            protosetOutOpt,
            outputOpt,
            forceOpt,
            revocationModeOpt,
            exportableKeyOpt,
            keepaliveTimeOpt,
            keepaliveTimeoutOpt,
            protoOpt,
            importPathOpt,
            protoOutDirOpt
        };

        // Use ParseResult to handle parameters
        command.SetAction(async (parseResult, _) =>
        {
            var address = parseResult.GetValue(addressArg);
            var symbol = parseResult.GetValue(symbolArg);
            var protosets = parseResult.GetValue(protosetOpt) ?? [];
            var plaintext = parseResult.GetValue(plaintextOpt);
            var insecure = parseResult.GetValue(insecureOpt);
            var cacert = parseResult.GetValue(cacertOpt);
            var cert = parseResult.GetValue(certOpt);
            var key = parseResult.GetValue(keyOpt);
            var certPassword = parseResult.GetValue(certPasswordOpt);
            var connectTimeout = parseResult.GetValue(connectTimeoutOpt);
            var maxTime = parseResult.GetValue(maxTimeOpt);
            var authority = parseResult.GetValue(authorityOpt);
            var serverName = parseResult.GetValue(serverNameOpt);
            var verbose = parseResult.GetValue(verboseOpt);
            var veryVerbose = parseResult.GetValue(veryVerboseOpt);
            var userAgent = parseResult.GetValue(userAgentOpt);
            var headers = parseResult.GetValue(headerOpt) ?? [];
            var reflectHeaders = parseResult.GetValue(reflectHeaderOpt) ?? [];
            var msgTemplate = parseResult.GetValue(msgTemplateOpt);
            var protosetOut = parseResult.GetValue(protosetOutOpt);
            var output = parseResult.GetValue(outputOpt);
            var force = parseResult.GetValue(forceOpt);
            var revocationMode = parseResult.GetValue(revocationModeOpt);
            var exportableKey = parseResult.GetValue(exportableKeyOpt);
            var keepaliveTime = parseResult.GetValue(keepaliveTimeOpt);
            var keepaliveTimeout = parseResult.GetValue(keepaliveTimeoutOpt);
            var protoFiles = parseResult.GetValue(protoOpt) ?? [];
            var importPaths = parseResult.GetValue(importPathOpt) ?? [];
            var protoOutDir = parseResult.GetValue(protoOutDirOpt);

            try
            {
                await ExecuteAsync(
                    address,
                    symbol,
                    protosets,
                    plaintext,
                    insecure,
                    cacert,
                    cert,
                    key,
                    certPassword,
                    connectTimeout,
                    authority,
                    serverName,
                    verbose,
                    veryVerbose,
                    userAgent,
                    headers,
                    reflectHeaders,
                    msgTemplate,
                    protosetOut,
                    output,
                    force,
                    maxTime,
                    revocationMode,
                    protoFiles,
                    importPaths,
                    protoOutDir,
                    exportableKey,
                    keepaliveTime,
                    keepaliveTimeout);

                return 0;
            }
            catch (GrpcCommandException ex)
            {
                return ex.ExitCode;
            }
        });

        return command;
    }

    internal static void ValidateOptions(string? address, string[] protosets, bool plaintext, bool insecure, string? serverName, bool verbose, OutputFormat output = OutputFormat.Text, string[]? protoFiles = null)
    {
        var localSchemaCount = protosets.Length + (protoFiles?.Length ?? 0);

        switch (localSchemaCount)
        {
            // Validate required options
            case 0 when string.IsNullOrEmpty(address):

                ErrorRenderer.RenderAndThrow(new ErrorEnvelope
                {
                    Category = ErrorCategory.Usage,
                    ExitCode = 2,
                    Message = "Must specify either --protoset files, --proto files, or server address",
                    Suggestions =
                    [
                        "grpcn --protoset file.protoset describe",
                        "grpcn --proto file.proto describe MyService",
                        "grpcn localhost:9090 describe MyService"
                    ]
                }, output);

                break;

            // Warn about incompatible option combinations
            case > 0 when !string.IsNullOrEmpty(address):

            {
                if (verbose)
                {
                    Diagnostics.Markup("[yellow]Warning:[/] Both local schema files (--protoset/--proto) and address specified. Using local schema (server reflection will be ignored).");
                }

                break;
            }
        }

        // Warn about TLS-specific options used with --plaintext
        if (plaintext && (insecure || serverName is not null) && verbose)
        {
            Diagnostics.Markup("[yellow]Warning:[/] TLS options (--insecure, --servername) ignored when using --plaintext");
        }

        // Security warning for --insecure
        if (insecure && verbose)
        {
            Diagnostics.Markup("[yellow]Security Warning:[/] TLS certificate verification disabled (--insecure). Use only for testing!");
        }
    }

    internal static async Task ExecuteAsync(
        string? address,
        string? symbol,
        string[] protosets,
        bool plaintext,
        bool insecure,
        string? cacert,
        string? cert,
        string? key,
        string? certPassword,
        string? connectTimeout,
        string? authority,
        string? serverName,
        bool verbose,
        bool veryVerbose,
        string? userAgent,
        string[] headers,
        string[] reflectHeaders,
        bool msgTemplate,
        string? protosetOut,
        OutputFormat output = OutputFormat.Text,
        bool force = false,
        string? maxTime = null,
        string? revocationMode = null,
        string[]? protoFiles = null,
        string[]? importPaths = null,
        string? protoOutDir = null,
        bool exportableKey = false,
        string? keepaliveTime = null,
        string? keepaliveTimeout = null)
    {
        var startTime = DateTime.UtcNow;

        PositionalArgumentGuard.RejectOptionLikeValues("describe", output, ("address", address), ("symbol", symbol));

        var maxTimeSpan = maxTime is not null
            ? ErrorRenderer.ConvertOption(() => GrpcChannelFactory.ParseDuration(maxTime), output)
            : (TimeSpan?)null;

        using var deadlineCts = maxTimeSpan is not null
            ? new CancellationTokenSource(maxTimeSpan.Value)
            : new CancellationTokenSource();

        var operationToken = deadlineCts.Token;

        protoFiles ??= [];
        importPaths ??= [];

        var offlineSchema = protosets.Length > 0 || protoFiles.Length > 0;

        // When using local schema files without a server, the first positional arg (address) is actually the symbol
        if (offlineSchema && !string.IsNullOrEmpty(address) && string.IsNullOrEmpty(symbol))
        {
            symbol = address;
            address = null;
        }

        // Validate options before proceeding
        ValidateOptions(address, protosets, plaintext, insecure, serverName, verbose, output, protoFiles);

        // Create timing context if very verbose mode is enabled
        var timing = veryVerbose ? new TimingContext() : null;

        var channelOptions = new GrpcChannelFactory.ChannelOptions
        {
            Plaintext = plaintext,
            InsecureSkipVerify = insecure,
            CaCertPath = cacert,
            ClientCertPath = cert,
            ClientKeyPath = key,
            ClientCertPassword = certPassword,
            ConnectTimeout = connectTimeout is not null
                ? ErrorRenderer.ConvertOption(() => GrpcChannelFactory.ParseDuration(connectTimeout), output)
                : null,
            KeepaliveTime = keepaliveTime is not null
                ? ErrorRenderer.ConvertOption(() => GrpcChannelFactory.ParseDuration(keepaliveTime), output)
                : null,
            KeepaliveTimeout = keepaliveTimeout is not null
                ? ErrorRenderer.ConvertOption(() => GrpcChannelFactory.ParseDuration(keepaliveTimeout), output)
                : null,
            Authority = authority,
            ServerName = serverName,
            RevocationMode = ErrorRenderer.ConvertOption(() => GrpcChannelFactory.ParseRevocationMode(revocationMode), output),
            ExportableClientKey = exportableKey
        };

        var reflectionMetadata = GrpcChannelFactory.CreateMetadata(
            headers.Concat(reflectHeaders),
            userAgent);

        if (verbose && !string.IsNullOrEmpty(address) && !offlineSchema)
        {
            Diagnostics.Markup($"[dim]Connecting to {address}...[/]");
            Diagnostics.Markup($"[dim]Protocol: {(plaintext ? "HTTP/2 (plaintext)" : "HTTP/2 (TLS)")}[/]");

            if (insecure)
            {
                Diagnostics.Markup("[dim]TLS verification: Disabled (--insecure)[/]");
            }

            if (connectTimeout is not null)
            {
                Diagnostics.Markup($"[dim]Connection timeout: {connectTimeout}[/]");
            }

            if (maxTime is not null)
            {
                Diagnostics.Markup($"[dim]Operation timeout: {maxTime}[/]");
            }

            if (authority is not null)
            {
                Diagnostics.Markup($"[dim]Authority: {authority}[/]");
            }
        }

        try
        {
            timing?.StartPhase(offlineSchema ? "Schema Loading" : "Connection Establishment");

            await using var session = await DescriptorSourceFactory.CreateAsync(
                address,
                protosets,
                protoFiles,
                importPaths,
                channelOptions,
                reflectionMetadata,
                operationToken);

            var descriptorSource = session.Source;

            switch (verbose)
            {
                case true when offlineSchema:

                    Diagnostics.Markup("[dim]Local schema files loaded successfully[/]");

                    break;

                case true:

                    Diagnostics.Markup("[dim]Connected successfully, querying server reflection...[/]");

                    break;
            }

            timing?.StartPhase("Schema Discovery");

            if (string.IsNullOrEmpty(symbol))
            {
                if (verbose)
                {
                    Diagnostics.Markup("[dim]Describing all services...[/]");
                }

                var services = await descriptorSource.ListServicesAsync(operationToken);

                if (verbose)
                {
                    Diagnostics.Markup($"[dim]Found {services.Count} service(s) to describe[/]");
                }

                foreach (var svc in services)
                {
                    await DescribeSymbolAsync(descriptorSource, svc, verbose, msgTemplate, output, operationToken);

                    // Blank separator between services in text mode; NDJSON in json mode needs no separator.
                    if (output == OutputFormat.Text)
                    {
                        AnsiConsole.WriteLine();
                    }
                }

                // Export all services if --protoset-out specified
                if (!string.IsNullOrEmpty(protosetOut))
                {
                    await ProtosetExporter.WriteProtosetAsync(descriptorSource, protosetOut, force, [.. services], operationToken);

                    if (verbose)
                    {
                        Diagnostics.Markup($"[dim]Wrote protoset to {protosetOut}[/]");
                    }
                }
            }
            else
            {
                if (verbose)
                {
                    Diagnostics.Markup($"[dim]Describing symbol '{symbol}'...[/]");
                }

                await DescribeSymbolAsync(descriptorSource, symbol, verbose, msgTemplate, output, operationToken);

                // Export the specific symbol if --protoset-out specified
                if (!string.IsNullOrEmpty(protosetOut))
                {
                    await ProtosetExporter.WriteProtosetAsync(descriptorSource, protosetOut, force, [symbol], operationToken);

                    if (verbose)
                    {
                        Diagnostics.Markup($"[dim]Wrote protoset to {protosetOut}[/]");
                    }
                }
            }

            // Export reconstructed .proto sources if --proto-out-dir specified.
            if (!string.IsNullOrEmpty(protoOutDir))
            {
                await ProtoFileEmitter.WriteAsync(descriptorSource, protoOutDir, force, operationToken);

                if (verbose)
                {
                    Diagnostics.Markup($"[dim]Wrote .proto files to {protoOutDir}[/]");
                }
            }

            // Print timing summary if very verbose mode
            timing?.PrintSummary();

            if (verbose)
            {
                var duration = DateTime.UtcNow - startTime;

                Diagnostics.Markup($"[dim]Operation completed in {duration.TotalMilliseconds:F0}ms[/]");
            }
        }
        catch (GrpcCommandException)
        {
            throw;
        }
        catch (ProtocNotFoundException ex)
        {
            ErrorRenderer.RenderAndThrow(new ErrorEnvelope
            {
                Category = ErrorCategory.Schema,
                ExitCode = 3,
                Message = ex.Message,
                Suggestions =
                [
                    "Install protoc and ensure it is on PATH",
                    "Alternative: pre-compile a protoset and pass --protoset instead of --proto"
                ]
            }, output);
        }
        catch (FileNotFoundException ex)
        {
            ErrorRenderer.RenderAndThrow(new ErrorEnvelope
            {
                Category = ErrorCategory.Schema,
                ExitCode = 3,
                Message = $"Protoset file not found: {ex.FileName ?? ex.Message}",
                Suggestions =
                [
                    "Check the file path is correct",
                    "Ensure the file has a .protoset or .pb extension",
                    "Generate protoset using: protoc --descriptor_set_out=file.protoset --include_imports file.proto"
                ]
            }, output);
        }
        catch (RpcException ex)
        {
            var suggestions = ex.StatusCode switch
            {
                StatusCode.Unavailable => new[]
                {
                    "Ensure the server is running",
                    "Try adding --plaintext if server doesn't use TLS",
                    "Check firewall settings",
                    "Verify the address and port are correct"
                },
                StatusCode.Unimplemented =>
                [
                    "Server does not support reflection",
                    "Use --protoset to provide schema files instead",
                    "Ask server admin to enable grpc-reflection"
                ],
                StatusCode.NotFound =>
                [
                    "Use 'list' command to see available services",
                    "Check the symbol name spelling and case",
                    "Ensure the symbol is fully qualified (e.g., package.Service)"
                ],
                _ => []
            };

            ErrorRenderer.RenderAndThrow(new ErrorEnvelope
            {
                Category = ErrorCategory.Rpc,
                ExitCode = 64 + (int)ex.Status.StatusCode,
                Message = ex.Status.Detail,
                Address = address,
                Suggestions = suggestions,
                Grpc = new RpcErrorInfo
                {
                    Code = (int)ex.Status.StatusCode,
                    Status = ex.StatusCode.ToString(),
                    Detail = ex.Status.Detail
                }
            }, output);
        }
        catch (HttpRequestException ex)
        {
            ErrorRenderer.RenderAndThrow(new ErrorEnvelope
            {
                Category = ErrorCategory.Network,
                ExitCode = 4,
                Message = $"Failed to connect to {address ?? string.Empty}: {ex.Message}",
                Address = address,
                Suggestions =
                [
                    "Ensure the server is running and accessible",
                    "Try adding --plaintext if server uses HTTP/2 without TLS",
                    "Check if a proxy or firewall is blocking the connection"
                ]
            }, output);
        }
        catch (TimeoutException ex)
        {
            ErrorRenderer.RenderAndThrow(new ErrorEnvelope
            {
                Category = ErrorCategory.Timeout,
                ExitCode = 5,
                Message = $"Connection to {address ?? string.Empty} timed out",
                Address = address,
                Hint = BuildTimeoutHint(connectTimeout, verbose, ex.Message),
                Suggestions =
                [
                    "Increase timeout with --connect-timeout (e.g., --connect-timeout 30s)",
                    "Check network connectivity",
                    "Verify server address is correct"
                ]
            }, output);
        }
        catch (OperationCanceledException ex) when (maxTime is not null && deadlineCts.IsCancellationRequested)
        {
            ErrorRenderer.RenderAndThrow(new ErrorEnvelope
            {
                Category = ErrorCategory.Timeout,
                ExitCode = 5,
                Message = $"Operation exceeded --max-time ({maxTime})",
                Address = address,
                Hint = verbose ? ex.Message : null,
                Suggestions =
                [
                    "Increase --max-time for large schemas or slow reflection services",
                    "Use --protoset for offline schema discovery when possible"
                ]
            }, output);
        }
        catch (InvalidDataException ex)
        {
            ErrorRenderer.RenderAndThrow(new ErrorEnvelope
            {
                Category = ErrorCategory.Schema,
                ExitCode = 3,
                Message = $"Invalid protoset file format: {ex.Message}",
                Suggestions =
                [
                    "Ensure the file is a valid FileDescriptorSet",
                    "Regenerate using: protoc --descriptor_set_out=file.protoset --include_imports file.proto"
                ]
            }, output);
        }
        catch (IOException ex)
        {
            ErrorRenderer.RenderAndThrow(new ErrorEnvelope
            {
                Category = ErrorCategory.Schema,
                ExitCode = 3,
                Message = ex.Message
            }, output);
        }
        catch (Exception ex)
        {
            ErrorRenderer.RenderAndThrow(new ErrorEnvelope
            {
                Category = ErrorCategory.Internal,
                ExitCode = 1,
                Message = ex.Message,
                Hint = verbose ? ex.StackTrace : null
            }, output);
        }
    }

    internal static async Task DescribeSymbolAsync(
        IDescriptorSource descriptorSource,
        string symbolName,
        bool verbose,
        bool msgTemplate,
        OutputFormat output = OutputFormat.Text,
        CancellationToken cancellationToken = default)
    {
        var descriptor = await descriptorSource.FindSymbolAsync(symbolName, cancellationToken);

        if (descriptor is null)
        {
            var envelope = new ErrorEnvelope
            {
                Category = ErrorCategory.Schema,
                ExitCode = 3,
                Message = $"Symbol '{symbolName}' not found"
            };

            ErrorRenderer.Render(envelope, output);

            throw new GrpcCommandException(envelope.Message, envelope.ExitCode, true) { Envelope = envelope };
        }

        if (verbose)
        {
            var descriptorType = descriptor switch
            {
                ServiceDescriptor => "Service",
                MessageDescriptor => "Message",
                EnumDescriptor    => "Enum",
                _                 => "Symbol"
            };

            Diagnostics.Markup($"[dim]Found {descriptorType}: {descriptor.FullName}[/]");
        }

        // JSON mode short-circuits to OutputRenderer; text mode falls through to the proto-syntax printers below.
        if (output == OutputFormat.Json)
        {
            OutputRenderer.WriteDescribeJson(descriptor, msgTemplate);

            return;
        }

        // If --msg-template is specified, print proto definition then JSON template for message types
        if (msgTemplate && descriptor is MessageDescriptor msgTmplDesc)
        {
            PrintMessageDefinition(msgTmplDesc, "");

            Console.WriteLine();
            Console.WriteLine("Message template:");

            Console.WriteLine(MessageTemplateGenerator.GenerateJson(msgTmplDesc));

            return;
        }

        switch (descriptor)
        {
            case ServiceDescriptor svc:

            {
                Console.WriteLine($"{svc.FullName} is a service:");
                Console.WriteLine($"service {svc.Name} {{");

                foreach (var method in svc.Methods.OrderBy(m => m.Name))
                {
                    var inputStream = method.IsClientStreaming ? "stream " : "";
                    var outputStream = method.IsServerStreaming ? "stream " : "";

                    Console.WriteLine($"  rpc {method.Name} ( {inputStream}.{method.InputType.FullName} ) returns ( {outputStream}.{method.OutputType.FullName} );");
                }

                Console.WriteLine("}");

                break;
            }

            case MethodDescriptor method:

            {
                var inputStream = method.IsClientStreaming ? "stream " : "";
                var outputStream = method.IsServerStreaming ? "stream " : "";

                Console.WriteLine($"{method.FullName} is a method:");
                Console.WriteLine($"  rpc {method.Name} ( {inputStream}.{method.InputType.FullName} ) returns ( {outputStream}.{method.OutputType.FullName} );");

                break;
            }

            case MessageDescriptor msg:

            {
                PrintMessageDefinition(msg, "");

                break;
            }

            case EnumDescriptor enm:

            {
                PrintEnumDefinition(enm, "");

                break;
            }

            default:

                Console.WriteLine($"{descriptor.FullName} is a {descriptor.GetType().Name}");

                break;
        }
    }

    internal static void PrintMessageDefinition(MessageDescriptor msg, string indent)
    {
        Console.WriteLine($"{msg.FullName} is a message:");
        Console.WriteLine($"{indent}message {msg.Name} {{");

        // Collect oneof groups
        var oneofFields = new HashSet<FieldDescriptor>();

        foreach (var oneof in msg.Oneofs)
        {
            foreach (var f in oneof.Fields)
            {
                _ = oneofFields.Add(f);
            }
        }

        // Print fields (non-oneof) and oneof blocks
        var printedOneofs = new HashSet<string>();

        foreach (var field in msg.Fields.InDeclarationOrder())
        {
            if (oneofFields.Contains(field))
            {
                // Print the oneof block once when we encounter its first field
                var oneof = field.ContainingOneof;

                if (!printedOneofs.Add(oneof.Name))
                {
                    continue;
                }

                Console.WriteLine($"{indent}  oneof {oneof.Name} {{");

                foreach (var oneofField in oneof.Fields)
                {
                    var typeName = GetProtoTypeName(oneofField);

                    Console.WriteLine($"{indent}    {typeName} {oneofField.Name} = {oneofField.FieldNumber};");
                }

                Console.WriteLine($"{indent}  }}");
            }
            else
            {
                var typeName = GetProtoTypeName(field);
                var label = field is { IsRepeated: true, IsMap: false }
                    ? "repeated "
                    : "";

                Console.WriteLine($"{indent}  {label}{typeName} {field.Name} = {field.FieldNumber};");
            }
        }

        // Print nested enums (skip synthetic map entry types)
        foreach (var nestedEnum in msg.EnumTypes)
        {
            PrintEnumDefinition(nestedEnum, indent + "  ");
        }

        // Print nested messages (skip synthetic map entry types)
        foreach (var nestedMsg in msg.NestedTypes)
        {
            if (nestedMsg.GetOptions()?.MapEntry == true)
            {
                continue;
            }

            PrintNestedMessageDefinition(nestedMsg, indent + "  ");
        }

        Console.WriteLine($"{indent}}}");
    }

    internal static void PrintNestedMessageDefinition(MessageDescriptor msg, string indent)
    {
        Console.WriteLine($"{indent}message {msg.Name} {{");

        foreach (var field in msg.Fields.InDeclarationOrder())
        {
            var typeName = GetProtoTypeName(field);
            var label = field is { IsRepeated: true, IsMap: false }
                ? "repeated "
                : "";

            Console.WriteLine($"{indent}  {label}{typeName} {field.Name} = {field.FieldNumber};");
        }

        Console.WriteLine($"{indent}}}");
    }

    internal static void PrintEnumDefinition(EnumDescriptor enm, string indent)
    {
        Console.WriteLine($"{enm.FullName} is an enum:");
        Console.WriteLine($"{indent}enum {enm.Name} {{");

        foreach (var value in enm.Values)
        {
            Console.WriteLine($"{indent}  {value.Name} = {value.Number};");
        }

        Console.WriteLine($"{indent}}}");
    }

    internal static string GetProtoTypeName(FieldDescriptor field)
    {
        if (!field.IsMap)
        {
            return field.FieldType switch
            {
                FieldType.Message => $".{field.MessageType.FullName}",
                FieldType.Enum    => $".{field.EnumType.FullName}",
                _                 => GetScalarTypeName(field)
            };
        }

        var mapDescriptor = field.MessageType;
        var keyField = mapDescriptor.FindFieldByNumber(1);
        var valueField = mapDescriptor.FindFieldByNumber(2);

        return $"map<{GetScalarTypeName(keyField)}, {GetScalarTypeName(valueField)}>";
    }

    internal static string GetScalarTypeName(FieldDescriptor field)
        => field.FieldType switch
        {
            FieldType.Double   => "double",
            FieldType.Float    => "float",
            FieldType.Int64    => "int64",
            FieldType.UInt64   => "uint64",
            FieldType.Int32    => "int32",
            FieldType.Fixed64  => "fixed64",
            FieldType.Fixed32  => "fixed32",
            FieldType.Bool     => "bool",
            FieldType.String   => "string",
            FieldType.Bytes    => "bytes",
            FieldType.UInt32   => "uint32",
            FieldType.SFixed32 => "sfixed32",
            FieldType.SFixed64 => "sfixed64",
            FieldType.SInt32   => "sint32",
            FieldType.SInt64   => "sint64",
            FieldType.Enum     => $".{field.EnumType.FullName}",
            FieldType.Message  => $".{field.MessageType.FullName}",
            _                  => field.FieldType.ToString().ToLowerInvariant()
        };

    private static string? BuildTimeoutHint(string? connectTimeout, bool verbose, string exceptionMessage)
    {
        if (connectTimeout is not null)
        {
            return $"Connection timeout was set to: {connectTimeout}";
        }

        return verbose ? exceptionMessage : null;
    }
}
