using Google.Protobuf;
using Google.Protobuf.Reflection;
using Grpc.Core;
using GrpCurl.Net.DescriptorSources;
using GrpCurl.Net.Exceptions;
using GrpCurl.Net.Invocation;
using GrpCurl.Net.Utilities;
using Spectre.Console;
using System.CommandLine;
using System.Text.Json;

namespace GrpCurl.Net.Commands;

internal static class InvokeCommandHandler
{
    public static Command Create()
    {
        var addressArg = new Argument<string>("address")
        {
            Description = "Server address (host:port)"
        };

        var methodArg = new Argument<string>("method")
        {
            Description = "Method to invoke (Service/Method)"
        };

        var dataOpt = new Option<string>("--data", "-d")
        {
            Description = "Request data in JSON",
            DefaultValueFactory = _ => "{}"
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

        var headerOpt = new Option<string[]>("--header", "-H")
        {
            Description = "Headers (name: value)",
            Arity = ArgumentArity.ZeroOrMore
        };

        var verboseOpt = new Option<bool>("--verbose", "-v")
        {
            Description = "Verbose output"
        };

        var veryVerboseOpt = new Option<bool>("--very-verbose", "--vv")
        {
            Description = "Very verbose output with detailed timing information"
        };

        var emitDefaultsOpt = new Option<bool>("--emit-defaults")
        {
            Description = "Emit default values in JSON output"
        };

        var connectTimeoutOpt = new Option<string?>("--connect-timeout")
        {
            Description = "Connection timeout (e.g., '10s', '1m', '500ms'). Default: 10s"
        };

        var maxMsgSzOpt = new Option<string?>("--max-msg-sz")
        {
            Description = "Maximum message size (e.g., '4MB', '10MB'). Default: 4MB"
        };

        var maxTimeOpt = new Option<string?>("--max-time")
        {
            Description = "Maximum time for operation (e.g., '30s', '5m'). Sets gRPC deadline."
        };

        var authorityOpt = new Option<string?>("--authority")
        {
            Description = "Value to use for :authority header and TLS server name"
        };

        var serverNameOpt = new Option<string?>("--servername")
        {
            Description = "Override TLS server name for certificate validation"
        };

        var userAgentOpt = new Option<string?>("--user-agent")
        {
            Description = $"User-Agent header value. Default: {UserAgentProvider.Default}"
        };

        var allowUnknownFieldsOpt = new Option<bool>("--allow-unknown-fields")
        {
            Description = "Allow unknown fields in JSON requests (skip instead of error)"
        };

        var reflectHeaderOpt = new Option<string[]>("--reflect-header")
        {
            Description = "Headers for reflection requests only (name: value)",
            Arity = ArgumentArity.ZeroOrMore
        };

        var rpcHeaderOpt = new Option<string[]>("--rpc-header")
        {
            Description = "Headers for RPC requests only (name: value)",
            Arity = ArgumentArity.ZeroOrMore
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

        var command = new Command("invoke", CommandDescriptions.Invoke)
        {
            addressArg,
            methodArg,
            dataOpt,
            protosetOpt,
            plaintextOpt,
            insecureOpt,
            cacertOpt,
            certOpt,
            keyOpt,
            certPasswordOpt,
            headerOpt,
            verboseOpt,
            veryVerboseOpt,
            emitDefaultsOpt,
            connectTimeoutOpt,
            maxMsgSzOpt,
            maxTimeOpt,
            authorityOpt,
            serverNameOpt,
            userAgentOpt,
            allowUnknownFieldsOpt,
            reflectHeaderOpt,
            rpcHeaderOpt,
            protosetOutOpt,
            outputOpt,
            forceOpt
        };

        // Use ParseResult to handle parameters
        command.SetAction(async (parseResult, _) =>
        {
            var address = parseResult.GetValue(addressArg);
            var method = parseResult.GetValue(methodArg);
            var data = parseResult.GetValue(dataOpt);
            var protosets = parseResult.GetValue(protosetOpt);
            var plaintext = parseResult.GetValue(plaintextOpt);
            var insecure = parseResult.GetValue(insecureOpt);
            var cacert = parseResult.GetValue(cacertOpt);
            var cert = parseResult.GetValue(certOpt);
            var key = parseResult.GetValue(keyOpt);
            var certPassword = parseResult.GetValue(certPasswordOpt);
            var headerStrings = parseResult.GetValue(headerOpt);
            var verbose = parseResult.GetValue(verboseOpt);
            var veryVerbose = parseResult.GetValue(veryVerboseOpt);
            var emitDefaults = parseResult.GetValue(emitDefaultsOpt);
            var connectTimeout = parseResult.GetValue(connectTimeoutOpt);
            var maxMsgSz = parseResult.GetValue(maxMsgSzOpt);
            var maxTime = parseResult.GetValue(maxTimeOpt);
            var authority = parseResult.GetValue(authorityOpt);
            var serverName = parseResult.GetValue(serverNameOpt);
            var userAgent = parseResult.GetValue(userAgentOpt);
            var allowUnknownFields = parseResult.GetValue(allowUnknownFieldsOpt);
            var reflectHeaders = parseResult.GetValue(reflectHeaderOpt);
            var rpcHeaders = parseResult.GetValue(rpcHeaderOpt);
            var protosetOut = parseResult.GetValue(protosetOutOpt);
            var output = parseResult.GetValue(outputOpt);
            var force = parseResult.GetValue(forceOpt);

            try
            {
                await ExecuteAsync(
                    address!,
                    method!,
                    data,
                    protosets,
                    plaintext,
                    insecure,
                    cacert,
                    cert,
                    key,
                    certPassword,
                    headerStrings,
                    verbose,
                    veryVerbose,
                    emitDefaults,
                    connectTimeout,
                    maxMsgSz,
                    maxTime,
                    authority,
                    serverName,
                    userAgent,
                    allowUnknownFields,
                    reflectHeaders,
                    rpcHeaders,
                    protosetOut,
                    output,
                    force);

                return 0;
            }
            catch (GrpcCommandException ex)
            {
                return ex.ExitCode;
            }
        });

        return command;
    }

    internal static void ValidateOptions(bool plaintext, bool insecure, string? serverName, string? maxMsgSz, bool verbose)
    {
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

        if (maxMsgSz is null || !verbose)
        {
            return;
        }

        // Warn about large message sizes
        var size = GrpcChannelFactory.ParseSize(maxMsgSz);

        if (size > 10 * 1024 * 1024) // > 10MB
        {
            Diagnostics.Markup($"[yellow]Warning:[/] Large message size configured ({maxMsgSz}). This may impact memory usage.");
        }
    }

    internal static async Task ExecuteAsync(
        string address,
        string methodName,
        string? data,
        string[]? protosets,
        bool plaintext,
        bool insecure,
        string? cacert,
        string? cert,
        string? key,
        string? certPassword,
        string[]? headerStrings,
        bool verbose,
        bool veryVerbose,
        bool emitDefaults,
        string? connectTimeout,
        string? maxMsgSz,
        string? maxTime,
        string? authority,
        string? serverName,
        string? userAgent,
        bool allowUnknownFields,
        string[]? reflectHeaders,
        string[]? rpcHeaders,
        string? protosetOut,
        OutputFormat output = OutputFormat.Text,
        bool force = false)
    {
        // Validate options before proceeding
        ValidateOptions(plaintext, insecure, serverName, maxMsgSz, verbose);

        // Create timing context if very verbose mode is enabled
        var timing = veryVerbose ? new TimingContext() : null;

        // Create cancellation token sources for Ctrl+C handling and deadline
        using var ctrlCCts = new CancellationTokenSource();

        CancellationTokenSource? deadlineCts = null;
        var cancelHandler = CancelHandler();

        Console.CancelKeyPress += cancelHandler;

        try
        {
            var parts = methodName.Replace('/', '.').Split('.');

            if (parts.Length < 2)
            {
                ErrorRenderer.RenderAndThrow(new ErrorEnvelope
                {
                    Category = ErrorCategory.Usage,
                    ExitCode = 2,
                    Message = "Method must be in format 'Service/Method'"
                }, output);
            }

            var serviceName = string.Join(".", parts.Take(parts.Length - 1));
            var methodShortName = parts[^1];

            // Parse message size early so it can be applied to both reflection and RPC channels
            int? maxReceiveSize = null;
            int? maxSendSize = null;

            if (maxMsgSz is not null)
            {
                var parsedSize = GrpcChannelFactory.ParseSize(maxMsgSz);

                maxReceiveSize = parsedSize;
                maxSendSize = parsedSize;
            }

            IDescriptorSource descriptorSource;

            if (protosets is { Length: > 0 })
            {
                timing?.StartPhase("Protoset Loading");
                descriptorSource = await ProtosetSource.LoadFromFilesAsync(protosets, deadlineCts?.Token ?? CancellationToken.None);
            }
            else
            {
                timing?.StartPhase("Connection Establishment");

                var channelOptions = new GrpcChannelFactory.ChannelOptions
                {
                    Plaintext = plaintext,
                    InsecureSkipVerify = insecure,
                    CaCertPath = cacert,
                    ClientCertPath = cert,
                    ClientKeyPath = key,
                    ClientCertPassword = certPassword,
                    ConnectTimeout = connectTimeout is not null ? GrpcChannelFactory.ParseDuration(connectTimeout) : null,
                    MaxReceiveMessageSize = maxReceiveSize,
                    MaxSendMessageSize = maxSendSize,
                    Authority = authority,
                    ServerName = serverName
                };

                var channel = GrpcChannelFactory.Create(address, channelOptions);

                // Create reflection metadata by merging -H headers with --reflect-header
                var reflectionMetadata = GrpcChannelFactory.CreateMetadata(
                    (headerStrings ?? []).Concat(reflectHeaders ?? []),
                    userAgent);

                descriptorSource = new ReflectionSource(channel, reflectionMetadata, true);
            }

            timing?.StartPhase("Schema Discovery");

            var serviceDescriptor = await descriptorSource.FindSymbolAsync(serviceName, deadlineCts?.Token ?? CancellationToken.None);

            if (serviceDescriptor is not ServiceDescriptor svc)
            {
                ErrorRenderer.RenderAndThrow(new ErrorEnvelope
                {
                    Category = ErrorCategory.Schema,
                    ExitCode = 3,
                    Message = $"Service '{serviceName}' not found",
                    Address = address
                }, output);

                return;
            }

            var methodDescriptor = svc.Methods.FirstOrDefault(m => m.Name == methodShortName);

            if (methodDescriptor is null)
            {
                ErrorRenderer.RenderAndThrow(new ErrorEnvelope
                {
                    Category = ErrorCategory.Schema,
                    ExitCode = 3,
                    Message = $"Method '{methodShortName}' not found",
                    Address = address,
                    Method = methodName
                }, output);
            }

            timing?.StartPhase("Request Preparation");

            // Refuse to read from a TTY — would block forever waiting for input.
            if (data == "@" && !ConsoleEnvironment.IsInputRedirected)
            {
                ErrorRenderer.RenderAndThrow(new ErrorEnvelope
                {
                    Category = ErrorCategory.Usage,
                    ExitCode = 2,
                    Message = "--data @ requires stdin to be redirected (piped or from a file); not connected to a TTY",
                    Suggestions =
                    [
                        $"Pipe input: printf '{{}}' | grpcurl.net invoke ... {methodName} --data @",
                        $"Or pass inline: grpcurl.net invoke ... {methodName} --data '{{...}}'",
                        "For client/bidi streaming, use a JSON array: --data '[{...},{...}]'"
                    ]
                }, output);
            }

            string? requestJson;

            // For streaming RPCs, pass "@" through to GenerateRequests so it can read stdin line-by-line
            // For unary/server-streaming, read all stdin upfront
            if (data == "@" && !methodDescriptor.IsClientStreaming)
            {
                using var reader = new StreamReader(Console.OpenStandardInput());

                requestJson = await reader.ReadToEndAsync(deadlineCts?.Token ?? CancellationToken.None);
            }
            else
            {
                requestJson = data;
            }

            timing?.StartPhase("RPC Channel Setup");

            var channelOptions2 = new GrpcChannelFactory.ChannelOptions
            {
                Plaintext = plaintext,
                InsecureSkipVerify = insecure,
                ConnectTimeout = connectTimeout is not null ? GrpcChannelFactory.ParseDuration(connectTimeout) : null,
                MaxReceiveMessageSize = maxReceiveSize,
                MaxSendMessageSize = maxSendSize,
                Authority = authority,
                ServerName = serverName
            };

            using var channel2 = GrpcChannelFactory.Create(address, channelOptions2);

            var invoker = new DynamicInvoker(channel2);

            // Create RPC metadata by merging -H headers with --rpc-header
            var metadata = GrpcChannelFactory.CreateMetadata(
                (headerStrings ?? []).Concat(rpcHeaders ?? []),
                userAgent);

            // Create linked cancellation token for both deadline (max-time) and Ctrl+C
            deadlineCts = maxTime is not null ? new CancellationTokenSource(GrpcChannelFactory.ParseDuration(maxTime)) : new CancellationTokenSource();

            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(deadlineCts.Token, ctrlCCts.Token);

            var cancellationToken = linkedCts.Token;

            // Calculate deadline for gRPC CallOptions
            DateTime? deadline = maxTime is not null ? DateTime.UtcNow.Add(GrpcChannelFactory.ParseDuration(maxTime)) : null;

            timing?.StartPhase("RPC Invocation");

            switch (methodDescriptor.IsClientStreaming)
            {
                case false when !methodDescriptor.IsServerStreaming:

                    await InvokeUnaryAsync(invoker, methodDescriptor, requestJson, metadata, verbose, emitDefaults, allowUnknownFields, deadline, timing, output, cancellationToken);

                    break;

                case false when methodDescriptor.IsServerStreaming:

                    await InvokeServerStreamingAsync(invoker, methodDescriptor, requestJson, metadata, verbose, emitDefaults, allowUnknownFields, deadline, timing, output, cancellationToken);

                    break;

                case true when !methodDescriptor.IsServerStreaming:

                    await InvokeClientStreamingAsync(invoker, methodDescriptor, requestJson, metadata, verbose, emitDefaults, allowUnknownFields, deadline, timing, output, cancellationToken);

                    break;

                default:

                    // Bidirectional streaming
                    await InvokeBidirectionalStreamingAsync(invoker, methodDescriptor, requestJson, metadata, verbose, emitDefaults, allowUnknownFields, deadline, timing, output, cancellationToken);

                    break;
            }

            // Export protoset if --protoset-out specified
            if (!string.IsNullOrEmpty(protosetOut))
            {
                await ProtosetExporter.WriteProtosetAsync(descriptorSource, protosetOut, force, [serviceName]);

                if (verbose)
                {
                    Diagnostics.Markup($"[dim]Wrote protoset to {protosetOut}[/]");
                }
            }

            // Print timing summary if very verbose mode
            timing?.PrintSummary();
        }
        catch (GrpcCommandException)
        {
            // Already rendered by ErrorRenderer.RenderAndThrow at the originating site.
            throw;
        }
        catch (FileNotFoundException ex)
        {
            ErrorRenderer.RenderAndThrow(new ErrorEnvelope
            {
                Category = ErrorCategory.Schema,
                ExitCode = 3,
                Message = $"Protoset file not found: {ex.FileName ?? string.Empty}",
                Suggestions =
                [
                    "Check the file path is correct",
                    "Ensure the file has a .protoset or .pb extension",
                    "Generate protoset using: protoc --descriptor_set_out=file.protoset --include_imports file.proto"
                ]
            }, output);
        }
        catch (JsonException ex)
        {
            ErrorRenderer.RenderAndThrow(new ErrorEnvelope
            {
                Category = ErrorCategory.Usage,
                ExitCode = 2,
                Message = $"Invalid JSON in request data: {ex.Message}",
                Suggestions =
                [
                    "Check JSON syntax (missing quotes, commas, brackets)",
                    "Ensure field names match protobuf definition",
                    "Use --msg-template with describe command to get correct format"
                ]
            }, output);
        }
        catch (OperationCanceledException ex)
        {
            if (ctrlCCts.IsCancellationRequested)
            {
                ErrorRenderer.RenderAndThrow(new ErrorEnvelope
                {
                    Category = ErrorCategory.Cancelled,
                    ExitCode = 130,
                    Message = "Operation cancelled by user"
                }, output);
            }
            else if (maxTime is not null && deadlineCts?.IsCancellationRequested == true)
            {
                ErrorRenderer.RenderAndThrow(new ErrorEnvelope
                {
                    Category = ErrorCategory.Timeout,
                    ExitCode = 5,
                    Message = $"Operation exceeded maximum time limit of {maxTime}",
                    Hint = verbose ? ex.Message : null
                }, output);
            }
            else
            {
                ErrorRenderer.RenderAndThrow(new ErrorEnvelope
                {
                    Category = ErrorCategory.Cancelled,
                    ExitCode = 130,
                    Message = $"Operation cancelled: {ex.Message}"
                }, output);
            }
        }
        catch (RpcException ex)
        {
            ErrorRenderer.RenderAndThrow(new ErrorEnvelope
            {
                Category = ErrorCategory.Rpc,
                ExitCode = 64 + (int)ex.Status.StatusCode,
                Message = ex.Status.Detail,
                Address = address,
                Method = methodName,
                Hint = ex.StatusCode == StatusCode.DeadlineExceeded && maxTime is not null
                    ? $"Maximum time was set to: {maxTime}"
                    : null,
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
                Message = $"Failed to connect to {address}: {ex.Message}",
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
                Message = $"Connection to {address} timed out",
                Address = address,
                Hint = connectTimeout is not null
                    ? $"Connection timeout was set to: {connectTimeout}"
                    : verbose ? ex.Message : null,
                Suggestions =
                [
                    "Increase timeout with --connect-timeout (e.g., --connect-timeout 30s)",
                    "Check network connectivity",
                    "Verify server address is correct"
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
        finally
        {
            // Unregister Ctrl+C handler to avoid memory leaks
            Console.CancelKeyPress -= cancelHandler;

            // Dispose deadline CancellationTokenSource
            deadlineCts?.Dispose();
        }

        return;

        ConsoleCancelEventHandler CancelHandler()
        {
            return (_, e) =>
            {
                e.Cancel = true; // Prevent immediate termination

                if (ctrlCCts.IsCancellationRequested)
                {
                    return;
                }

                Diagnostics.Markup("[yellow]Cancelling operation...[/]");

                ctrlCCts.Cancel();
            };
        }
    }

    private static async Task InvokeUnaryAsync(
        DynamicInvoker invoker,
        MethodDescriptor methodDescriptor,
        string? requestJson,
        Metadata metadata,
        bool verbose,
        bool emitDefaults,
        bool allowUnknownFields,
        DateTime? deadline,
        TimingContext? timing,
        OutputFormat output,
        CancellationToken cancellationToken)
    {
        timing?.StartPhase(CommandConstants.RequestSerialisation);

        var request = DynamicInvoker.CreateMessageFromJson(methodDescriptor.InputType, requestJson, allowUnknownFields);

        // Log unknown fields if verbose mode is enabled
        if (verbose && request is SimpleDynamicMessage { UnknownFields.Count: > 0 } dynamicRequest)
        {
            Diagnostics.Markup($"[yellow]Warning:[/] Request contains {dynamicRequest.UnknownFields.Count} unknown field(s): {Markup.Escape(string.Join(", ", dynamicRequest.UnknownFields))}");
        }

        if (verbose)
        {
            WriteVerboseMethodInfo(methodDescriptor, metadata);
        }

        timing?.StartPhase(CommandConstants.NetworkRoundTrip);

        var result = await invoker.InvokeUnaryAsync(methodDescriptor, request, metadata, deadline, cancellationToken);

        timing?.StartPhase(CommandConstants.ResponseDeserialization);

        if (timing is not null)
        {
            timing.RequestSizeBytes = request.CalculateSize();
            timing.ResponseSizeBytes = result.Response.CalculateSize();
            timing.MessageCount = 1;
        }

        if (verbose)
        {
            WriteVerboseResponseHeaders(result.ResponseHeaders);
            await Console.Error.WriteLineAsync("Response contents:");
        }

        OutputRenderer.WriteInvokeMessage(result.Response, index: 0, emitDefaults, output);

        if (verbose)
        {
            WriteVerboseResponseTrailers(result.ResponseTrailers);

            await Console.Error.WriteLineAsync("Sent 1 request and received 1 response");
        }
    }

    private static async Task InvokeServerStreamingAsync(
        DynamicInvoker invoker,
        MethodDescriptor methodDescriptor,
        string? requestJson,
        Metadata metadata,
        bool verbose,
        bool emitDefaults,
        bool allowUnknownFields,
        DateTime? deadline,
        TimingContext? timing,
        OutputFormat output,
        CancellationToken cancellationToken)
    {
        timing?.StartPhase(CommandConstants.RequestSerialisation);

        var request = DynamicInvoker.CreateMessageFromJson(methodDescriptor.InputType, requestJson, allowUnknownFields);

        // Log unknown fields if verbose mode is enabled
        if (verbose && request is SimpleDynamicMessage { UnknownFields.Count: > 0 } dynamicRequest)
        {
            Diagnostics.Markup($"[yellow]Warning:[/] Request contains {dynamicRequest.UnknownFields.Count} unknown field(s): {Markup.Escape(string.Join(", ", dynamicRequest.UnknownFields))}");
        }

        if (verbose)
        {
            Diagnostics.Markup("[dim]Starting server streaming...[/]");
        }

        timing?.StartPhase(CommandConstants.NetworkRoundTrip);

        var responseCount = 0;
        long totalResponseSize = 0;

        await foreach (var response in invoker.InvokeServerStreamingAsync(methodDescriptor, request, metadata, deadline, cancellationToken))
        {
            if (responseCount == 0)
            {
                timing?.StartPhase(CommandConstants.ResponseDeserialization);
            }

            OutputRenderer.WriteInvokeMessage(response, responseCount, emitDefaults, output);

            responseCount++;

            totalResponseSize += response.CalculateSize();
        }

        if (timing is not null)
        {
            timing.RequestSizeBytes = request.CalculateSize();
            timing.ResponseSizeBytes = totalResponseSize;
            timing.MessageCount = responseCount;
        }

        if (verbose)
        {
            Diagnostics.Markup($"[dim]Server streaming completed, received {responseCount} response(s)[/]");
        }
    }

    private static async Task InvokeClientStreamingAsync(
        DynamicInvoker invoker,
        MethodDescriptor methodDescriptor,
        string? requestJson,
        Metadata metadata,
        bool verbose,
        bool emitDefaults,
        bool allowUnknownFields,
        DateTime? deadline,
        TimingContext? timing,
        OutputFormat output,
        CancellationToken cancellationToken)
    {
        if (verbose)
        {
            Diagnostics.Markup("[dim]Starting client streaming...[/]");
        }

        timing?.StartPhase(CommandConstants.RequestSerialisation);

        var sentCount = 0;
        long totalRequestSize = 0;

        var response = await invoker.InvokeClientStreamingAsync(methodDescriptor, TrackRequests(), metadata, deadline, cancellationToken);

        timing?.StartPhase(CommandConstants.ResponseDeserialization);

        if (timing is not null)
        {
            timing.RequestSizeBytes = totalRequestSize;
            timing.ResponseSizeBytes = response.CalculateSize();
            timing.MessageCount = sentCount;
        }

        OutputRenderer.WriteInvokeMessage(response, index: 0, emitDefaults, output);

        if (verbose)
        {
            Diagnostics.Markup($"[dim]Client streaming completed, sent {sentCount} message(s)[/]");
        }

        return;

        async IAsyncEnumerable<IMessage> TrackRequests()
        {
            await foreach (var msg in GenerateRequests(requestJson, methodDescriptor.InputType, verbose, allowUnknownFields).WithCancellation(cancellationToken))
            {
                if (sentCount == 0)
                {
                    timing?.StartPhase(CommandConstants.NetworkRoundTrip);
                }

                sentCount++;
                totalRequestSize += msg.CalculateSize();

                yield return msg;
            }
        }
    }

    private static async Task InvokeBidirectionalStreamingAsync(
        DynamicInvoker invoker,
        MethodDescriptor methodDescriptor,
        string? requestJson,
        Metadata metadata,
        bool verbose,
        bool emitDefaults,
        bool allowUnknownFields,
        DateTime? deadline,
        TimingContext? timing,
        OutputFormat output,
        CancellationToken cancellationToken)
    {
        if (verbose)
        {
            Diagnostics.Markup("[dim]Starting bidirectional streaming...[/]");
        }

        timing?.StartPhase(CommandConstants.RequestSerialisation);

        var responseCount = 0;
        var sentCount = 0;
        long totalRequestSize = 0;
        long totalResponseSize = 0;

        await foreach (var response in invoker.InvokeDuplexStreamingAsync(methodDescriptor, TrackRequests(), metadata, deadline, cancellationToken))
        {
            if (responseCount == 0)
            {
                timing?.StartPhase(CommandConstants.ResponseDeserialization);
            }

            OutputRenderer.WriteInvokeMessage(response, responseCount, emitDefaults, output);

            responseCount++;
            totalResponseSize += response.CalculateSize();
        }

        if (timing is not null)
        {
            timing.RequestSizeBytes = totalRequestSize;
            timing.ResponseSizeBytes = totalResponseSize;
            timing.MessageCount = sentCount + responseCount;
        }

        if (verbose)
        {
            Diagnostics.Markup($"[dim]Bidirectional streaming completed, sent {sentCount} message(s), received {responseCount} response(s)[/]");
        }

        return;

        async IAsyncEnumerable<IMessage> TrackRequests()
        {
            await foreach (var msg in GenerateRequests(requestJson, methodDescriptor.InputType, verbose, allowUnknownFields).WithCancellation(cancellationToken))
            {
                if (sentCount == 0)
                {
                    timing?.StartPhase(CommandConstants.NetworkRoundTrip);
                }

                sentCount++;
                totalRequestSize += msg.CalculateSize();

                yield return msg;
            }
        }
    }

    internal static void WriteVerboseMethodInfo(MethodDescriptor methodDescriptor, Metadata metadata)
    {
        var inputStream = methodDescriptor.IsClientStreaming ? "stream " : "";
        var outputStream = methodDescriptor.IsServerStreaming ? "stream " : "";

        Console.Error.WriteLine("Resolved method descriptor:");
        Console.Error.WriteLine($"rpc {methodDescriptor.Name} ( {inputStream}.{methodDescriptor.InputType.FullName} ) returns ( {outputStream}.{methodDescriptor.OutputType.FullName} );");
        Console.Error.WriteLine();
        Console.Error.WriteLine("Request metadata to send:");

        if (metadata.Count == 0)
        {
            Console.Error.WriteLine("(empty)");
        }
        else
        {
            foreach (var entry in metadata)
            {
                Console.Error.WriteLine($"{entry.Key}: {entry.Value}");
            }
        }

        Console.Error.WriteLine();
    }

    internal static void WriteVerboseResponseHeaders(Metadata? headers)
    {
        Console.Error.WriteLine("Response headers received:");

        if (headers is null || headers.Count == 0)
        {
            Console.Error.WriteLine("(empty)");
        }
        else
        {
            foreach (var entry in headers)
            {
                if (!entry.IsBinary)
                {
                    Console.Error.WriteLine($"{entry.Key}: {entry.Value}");
                }
            }
        }

        Console.Error.WriteLine();
    }

    internal static void WriteVerboseResponseTrailers(Metadata? trailers)
    {
        Console.Error.WriteLine("Response trailers received:");

        if (trailers is null || trailers.Count == 0)
        {
            Console.Error.WriteLine("(empty)");
        }
        else
        {
            foreach (var entry in trailers)
            {
                if (!entry.IsBinary)
                {
                    Console.Error.WriteLine($"{entry.Key}: {entry.Value}");
                }
            }
        }
    }

    /// <summary>
    ///     Generates request messages from JSON input (supports single object, array, or stdin)
    /// </summary>
    private static async IAsyncEnumerable<IMessage> GenerateRequests(string? requestJson, MessageDescriptor inputType, bool verbose, bool allowUnknownFields)
    {
        // If data is from stdin (@), read JSON lines
        if (requestJson == "@")
        {
            if (verbose)
            {
                Diagnostics.Markup("[dim]Reading request messages from stdin (one JSON object per line, blank line or EOF to finish)...[/]");
            }

            using var reader = new StreamReader(Console.OpenStandardInput());

            while (await reader.ReadLineAsync() is { } line)
            {
                if (string.IsNullOrWhiteSpace(line))
                {
                    break;
                }

                var request = DynamicInvoker.CreateMessageFromJson(inputType, line, allowUnknownFields);

                // Log unknown fields if verbose mode is enabled
                if (verbose && request is SimpleDynamicMessage { UnknownFields.Count: > 0 } dynamicRequest)
                {
                    Diagnostics.Markup($"[yellow]Warning:[/] Request contains {dynamicRequest.UnknownFields.Count} unknown field(s): {Markup.Escape(string.Join(", ", dynamicRequest.UnknownFields))}");
                }

                if (verbose)
                {
                    Diagnostics.Markup($"[dim]Sending: {Markup.Escape(line)}[/]");
                }

                yield return request;
            }
        }
        else
        {
            // Single message mode - parse as JSON array, single object, or concatenated objects
            if (requestJson is null)
            {
                yield break;
            }

            // Try standard JSON parse first; fall back to concatenated objects if it fails
            List<string>? messages;

            try
            {
                var jsonDoc = JsonDocument.Parse(requestJson);
                var isArray = jsonDoc.RootElement.ValueKind == JsonValueKind.Array;

                if (isArray)
                {
                    messages = [];
                    messages.AddRange(jsonDoc.RootElement.EnumerateArray().Select(element => element.GetRawText()));
                }
                else
                {
                    messages = [requestJson];
                }
            }
            catch (JsonException)
            {
                // Standard parse failed -- try concatenated JSON objects (Go grpcurl format: {...} {...})
                messages = ParseConcatenatedJsonObjects(requestJson);
            }

            foreach (var msgJson in messages)
            {
                var request = DynamicInvoker.CreateMessageFromJson(inputType, msgJson, allowUnknownFields);

                // Log unknown fields if verbose mode is enabled
                if (verbose && request is SimpleDynamicMessage { UnknownFields.Count: > 0 } dynamicRequest)
                {
                    Diagnostics.Markup($"[yellow]Warning:[/] Request contains {dynamicRequest.UnknownFields.Count} unknown field(s): {Markup.Escape(string.Join(", ", dynamicRequest.UnknownFields))}");
                }

                if (verbose)
                {
                    Diagnostics.Markup($"[dim]Sending: {Markup.Escape(msgJson)}[/]");
                }

                yield return request;
            }
        }
    }

    /// <summary>
    ///     Parses a string that may contain one or more concatenated JSON objects.
    ///     Go grpcurl supports "{...} {...}" as multiple messages for streaming RPCs.
    /// </summary>
    internal static List<string> ParseConcatenatedJsonObjects(string json)
    {
        var results = new List<string>();
        var bytes = System.Text.Encoding.UTF8.GetBytes(json);
        var reader = new Utf8JsonReader(bytes, new JsonReaderOptions { AllowMultipleValues = true });
        var startIndex = 0;

        while (reader.Read())
        {
            switch (reader.TokenType)
            {
                case JsonTokenType.StartObject when reader.CurrentDepth == 0:

                    startIndex = (int)reader.TokenStartIndex;

                    break;

                case JsonTokenType.EndObject when reader.CurrentDepth == 0:

                    {
                        var length = (int)reader.BytesConsumed - startIndex;
                        var objectJson = System.Text.Encoding.UTF8.GetString(bytes, startIndex, length);
                        results.Add(objectJson);

                        break;
                    }

                case JsonTokenType.None:
                case JsonTokenType.StartObject:
                case JsonTokenType.EndObject:
                case JsonTokenType.StartArray:
                case JsonTokenType.EndArray:
                case JsonTokenType.PropertyName:
                case JsonTokenType.Comment:
                case JsonTokenType.String:
                case JsonTokenType.Number:
                case JsonTokenType.True:
                case JsonTokenType.False:
                case JsonTokenType.Null:

                default:
                    break;
            }
        }

        return results;
    }
}