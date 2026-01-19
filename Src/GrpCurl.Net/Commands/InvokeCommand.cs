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
using System.Text.Json.Serialization;

namespace GrpCurl.Net.Commands;

internal static class InvokeCommandHandler
{
    private static readonly JsonSerializerOptions JsonSerializerOptions = new()
    {
        WriteIndented = true
    };

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
            Description = "User-Agent header value. Default: grpcurl-dotnet/1.0.0"
        };

        var allowUnknownFieldsOpt = new Option<bool>("--allow-unknown-fields")
        {
            Description = "Allow unknown fields in JSON requests (skip instead of error)"
        };

        var formatErrorOpt = new Option<bool>("--format-error")
        {
            Description = "Format error responses as JSON instead of text"
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

        var command = new Command("invoke", "Invoke a gRPC method")
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
            formatErrorOpt,
            reflectHeaderOpt,
            rpcHeaderOpt,
            protosetOutOpt
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
            var formatError = parseResult.GetValue(formatErrorOpt);
            var reflectHeaders = parseResult.GetValue(reflectHeaderOpt);
            var rpcHeaders = parseResult.GetValue(rpcHeaderOpt);
            var protosetOut = parseResult.GetValue(protosetOutOpt);

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
                formatError,
                reflectHeaders,
                rpcHeaders,
                protosetOut);
        });

        return command;
    }

    private static void ValidateOptions(bool plaintext, bool insecure, string? serverName, string? maxMsgSz, bool verbose)
    {
        // Warn about TLS-specific options used with --plaintext
        if (plaintext && (insecure || serverName is not null) && verbose)
        {
            AnsiConsole.MarkupLine("[yellow]Warning:[/] TLS options (--insecure, --servername) ignored when using --plaintext");
        }

        // Security warning for --insecure
        if (insecure && verbose)
        {
            AnsiConsole.MarkupLine("[yellow]Security Warning:[/] TLS certificate verification disabled (--insecure). Use only for testing!");
        }

        if (maxMsgSz is null || !verbose)
        {
            return;
        }

        // Warn about large message sizes
        var size = GrpcChannelFactory.ParseSize(maxMsgSz);

        if (size > 10 * 1024 * 1024) // > 10MB
        {
            AnsiConsole.MarkupLine($"[yellow]Warning:[/] Large message size configured ({maxMsgSz}). This may impact memory usage.");
        }
    }

    private static async Task ExecuteAsync(
        string address,
        string methodName,
        string? data,
        string[]? protosets,
        bool plaintext,
        bool insecure,
        string? cacert,
        string? cert,
        string? key,
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
        bool formatError,
        string[]? reflectHeaders,
        string[]? rpcHeaders,
        string? protosetOut)
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
                AnsiConsole.MarkupLine("[red]Error:[/] Method must be in format 'Service/Method'");

                throw new GrpcCommandException(CommandConstants.CommandFailed);
            }

            var serviceName = string.Join(".", parts.Take(parts.Length - 1));
            var methodShortName = parts[^1];

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
                    ConnectTimeout = connectTimeout is not null ? GrpcChannelFactory.ParseDuration(connectTimeout) : null,
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
                AnsiConsole.MarkupLine($"[red]Error:[/] Service '{serviceName}' not found");

                throw new GrpcCommandException(CommandConstants.CommandFailed);
            }

            var methodDescriptor = svc.Methods.FirstOrDefault(m => m.Name == methodShortName);

            if (methodDescriptor is null)
            {
                AnsiConsole.MarkupLine($"[red]Error:[/] Method '{methodShortName}' not found");

                throw new GrpcCommandException(CommandConstants.CommandFailed);
            }

            timing?.StartPhase("Request Preparation");

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

            // Parse message size if specified
            int? maxReceiveSize = null;
            int? maxSendSize = null;

            if (maxMsgSz is not null)
            {
                var parsedSize = GrpcChannelFactory.ParseSize(maxMsgSz);

                maxReceiveSize = parsedSize;
                maxSendSize = parsedSize;
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

                    await InvokeUnaryAsync(invoker, methodDescriptor, requestJson, metadata, verbose, emitDefaults, allowUnknownFields, deadline, timing, cancellationToken);

                    break;

                case false when methodDescriptor.IsServerStreaming:

                    await InvokeServerStreamingAsync(invoker, methodDescriptor, requestJson, metadata, verbose, emitDefaults, allowUnknownFields, deadline, timing, cancellationToken);

                    break;

                case true when !methodDescriptor.IsServerStreaming:

                    await InvokeClientStreamingAsync(invoker, methodDescriptor, requestJson, metadata, verbose, emitDefaults, allowUnknownFields, deadline, timing, cancellationToken);

                    break;

                default:

                    // Bidirectional streaming
                    await InvokeBidirectionalStreamingAsync(invoker, methodDescriptor, requestJson, metadata, verbose, emitDefaults, allowUnknownFields, deadline, timing, cancellationToken);

                    break;
            }

            // Export protoset if --protoset-out specified
            if (!string.IsNullOrEmpty(protosetOut))
            {
                await ProtosetExporter.WriteProtosetAsync(descriptorSource, protosetOut, serviceName);

                if (verbose)
                {
                    AnsiConsole.MarkupLine($"[dim]Wrote protoset to {protosetOut}[/]");
                }
            }

            // Print timing summary if very verbose mode
            timing?.PrintSummary();

            if (verbose)
            {
                AnsiConsole.MarkupLine("[dim]Request completed successfully[/]");
            }
        }
        catch (FileNotFoundException ex)
        {
            AnsiConsole.MarkupLine($"[red]Error:[/] Protoset file not found: {ex.FileName}");
            AnsiConsole.MarkupLine(CommandConstants.Suggestions);
            AnsiConsole.MarkupLine("[dim]  - Check the file path is correct[/]");
            AnsiConsole.MarkupLine("[dim]  - Ensure the file has a .protoset or .pb extension[/]");
            AnsiConsole.MarkupLine("[dim]  - Generate protoset using: protoc --descriptor_set_out=file.protoset --include_imports file.proto[/]");

            throw new GrpcCommandException(CommandConstants.CommandFailed);
        }
        catch (JsonException ex)
        {
            AnsiConsole.MarkupLine("[red]JSON Error:[/] Invalid JSON in request data");
            AnsiConsole.MarkupLine($"[dim]{ex.Message}[/]");
            AnsiConsole.MarkupLine(CommandConstants.Suggestions);
            AnsiConsole.MarkupLine("[dim]  - Check JSON syntax (missing quotes, commas, brackets)[/]");
            AnsiConsole.MarkupLine("[dim]  - Ensure field names match protobuf definition[/]");
            AnsiConsole.MarkupLine("[dim]  - Use --msg-template with describe command to get correct format[/]");

            if (verbose)
            {
                Console.WriteLine(ex.StackTrace);
            }

            throw new GrpcCommandException(CommandConstants.CommandFailed);
        }
        catch (OperationCanceledException ex)
        {
            // Determine if cancellation was due to Ctrl+C or deadline timeout
            if (ctrlCCts.IsCancellationRequested)
            {
                // User pressed Ctrl+C
                AnsiConsole.MarkupLine("[yellow]Operation cancelled by user[/]");

                throw new GrpcCommandException("Operation cancelled by user", 130);
            }
            else if (maxTime is not null && deadlineCts?.IsCancellationRequested == true)
            {
                // Timeout due to max-time deadline
                AnsiConsole.MarkupLine($"[red]Timeout Error:[/] Operation exceeded maximum time limit of {maxTime}");

                if (verbose)
                {
                    AnsiConsole.MarkupLine($"[dim]{ex.Message}[/]");
                }

                throw new GrpcCommandException(CommandConstants.CommandFailed);
            }
            else
            {
                // Unknown cancellation source
                AnsiConsole.MarkupLine($"[red]Operation cancelled:[/] {ex.Message}");

                throw new GrpcCommandException(CommandConstants.CommandFailed);
            }
        }
        catch (RpcException ex)
        {
            if (formatError)
            {
                // Output error as JSON and exit directly
                // Using Environment.Exit avoids System.CommandLine's exception printing
                var errorJson = FormatErrorAsJson(ex, emitDefaults);
                Console.WriteLine(errorJson);
                Environment.Exit(64 + (int)ex.Status.StatusCode);
            }

            // Handle deadline exceeded specifically
            if (ex.StatusCode == StatusCode.DeadlineExceeded)
            {
                AnsiConsole.MarkupLine($"[red]Deadline Exceeded:[/] {ex.Status.Detail}");

                if (maxTime is not null)
                {
                    AnsiConsole.MarkupLine($"[dim]Maximum time was set to: {maxTime}[/]");
                }
            }
            else
            {
                AnsiConsole.MarkupLine($"[red]RPC Error:[/] {ex.Status.Detail}");
                AnsiConsole.MarkupLine($"[red]Status Code:[/] {ex.Status.StatusCode}");
            }

            throw new GrpcCommandException($"RPC error: {ex.Status.Detail}", 64 + (int)ex.Status.StatusCode);
        }
        catch (HttpRequestException ex)
        {
            AnsiConsole.MarkupLine($"[red]Connection Error:[/] Failed to connect to {address}");
            AnsiConsole.MarkupLine($"[dim]{ex.Message}[/]");
            AnsiConsole.MarkupLine(CommandConstants.Suggestions);
            AnsiConsole.MarkupLine("[dim]  - Ensure the server is running and accessible[/]");
            AnsiConsole.MarkupLine("[dim]  - Try adding --plaintext if server uses HTTP/2 without TLS[/]");
            AnsiConsole.MarkupLine("[dim]  - Check if a proxy or firewall is blocking the connection[/]");

            if (verbose)
            {
                Console.WriteLine(ex.StackTrace);
            }

            throw new GrpcCommandException(CommandConstants.CommandFailed);
        }
        catch (TimeoutException ex)
        {
            AnsiConsole.MarkupLine($"[red]Timeout Error:[/] Connection to {address} timed out");

            if (connectTimeout is not null)
            {
                AnsiConsole.MarkupLine($"[dim]Connection timeout was set to: {connectTimeout}[/]");
            }

            AnsiConsole.MarkupLine(CommandConstants.Suggestions);
            AnsiConsole.MarkupLine("[dim]  - Increase timeout with --connect-timeout (e.g., --connect-timeout 30s)[/]");
            AnsiConsole.MarkupLine("[dim]  - Check network connectivity[/]");
            AnsiConsole.MarkupLine("[dim]  - Verify server address is correct[/]");

            if (!verbose)
            {
                throw new GrpcCommandException(CommandConstants.CommandFailed);
            }

            AnsiConsole.MarkupLine($"[dim]{ex.Message}[/]");
            Console.WriteLine(ex.StackTrace);

            throw new GrpcCommandException(CommandConstants.CommandFailed);
        }
        catch (InvalidDataException ex)
        {
            AnsiConsole.MarkupLine("[red]Error:[/] Invalid protoset file format");
            AnsiConsole.MarkupLine($"[dim]{ex.Message}[/]");
            AnsiConsole.MarkupLine(CommandConstants.Suggestions);
            AnsiConsole.MarkupLine("[dim]  - Ensure the file is a valid FileDescriptorSet[/]");
            AnsiConsole.MarkupLine("[dim]  - Regenerate using: protoc --descriptor_set_out=file.protoset --include_imports file.proto[/]");

            if (verbose)
            {
                Console.WriteLine(ex.StackTrace);
            }

            throw new GrpcCommandException(CommandConstants.CommandFailed);
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]Error:[/] {ex.Message}");

            if (verbose)
            {
                Console.WriteLine(ex.StackTrace);
            }

            throw new GrpcCommandException(CommandConstants.CommandFailed);
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

                AnsiConsole.MarkupLine("[yellow]Cancelling operation...[/]");

                ctrlCCts.Cancel();
            };
        }
    }

    private static string FormatErrorAsJson(RpcException ex, bool emitDefaults)
    {
        var errorObj = new
        {
            error = new
            {
                code = (int)ex.StatusCode,
                message = ex.Status.Detail,
                status = ex.StatusCode.ToString()
            }
        };

        JsonSerializerOptions.DefaultIgnoreCondition = emitDefaults
            ? JsonIgnoreCondition.Never
            : JsonIgnoreCondition.WhenWritingDefault;

        var options = JsonSerializerOptions;

        return JsonSerializer.Serialize(errorObj, options);
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
        CancellationToken cancellationToken)
    {
        timing?.StartPhase(CommandConstants.RequestSerialisation);

        var request = DynamicInvoker.CreateMessageFromJson(methodDescriptor.InputType, requestJson, allowUnknownFields);

        // Log unknown fields if verbose mode is enabled
        if (verbose && request is SimpleDynamicMessage { UnknownFields.Count: > 0 } dynamicRequest)
        {
            AnsiConsole.MarkupLine($"[yellow]Warning:[/] Request contains {dynamicRequest.UnknownFields.Count} unknown field(s): {string.Join(", ", dynamicRequest.UnknownFields)}");
        }

        timing?.StartPhase(CommandConstants.NetworkRoundTrip);

        var response = await invoker.InvokeUnaryAsync(methodDescriptor, request, metadata, deadline, cancellationToken);

        timing?.StartPhase(CommandConstants.ResponseDeserialization);

        if (timing is not null)
        {
            timing.RequestSizeBytes = request.CalculateSize();
            timing.ResponseSizeBytes = response.CalculateSize();
            timing.MessageCount = 1;
        }

        var responseJson = DynamicInvoker.MessageToJson(response, emitDefaults);

        Console.WriteLine(responseJson);
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
        CancellationToken cancellationToken)
    {
        timing?.StartPhase(CommandConstants.RequestSerialisation);

        var request = DynamicInvoker.CreateMessageFromJson(methodDescriptor.InputType, requestJson, allowUnknownFields);

        // Log unknown fields if verbose mode is enabled
        if (verbose && request is SimpleDynamicMessage { UnknownFields.Count: > 0 } dynamicRequest)
        {
            AnsiConsole.MarkupLine($"[yellow]Warning:[/] Request contains {dynamicRequest.UnknownFields.Count} unknown field(s): {string.Join(", ", dynamicRequest.UnknownFields)}");
        }

        if (verbose)
        {
            AnsiConsole.MarkupLine("[dim]Starting server streaming...[/]");
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

            var responseJson = DynamicInvoker.MessageToJson(response, emitDefaults);

            Console.WriteLine(responseJson);

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
            AnsiConsole.MarkupLine($"[dim]Server streaming completed, received {responseCount} response(s)[/]");
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
        CancellationToken cancellationToken)
    {
        if (verbose)
        {
            AnsiConsole.MarkupLine("[dim]Starting client streaming...[/]");
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

        var responseJson = DynamicInvoker.MessageToJson(response, emitDefaults);

        Console.WriteLine(responseJson);

        if (verbose)
        {
            AnsiConsole.MarkupLine($"[dim]Client streaming completed, sent {sentCount} message(s)[/]");
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
        CancellationToken cancellationToken)
    {
        if (verbose)
        {
            AnsiConsole.MarkupLine("[dim]Starting bidirectional streaming...[/]");
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

            var responseJson = DynamicInvoker.MessageToJson(response, emitDefaults);

            Console.WriteLine(responseJson);

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
            AnsiConsole.MarkupLine($"[dim]Bidirectional streaming completed, sent {sentCount} message(s), received {responseCount} response(s)[/]");
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

    /// <summary>
    ///     Generates request messages from JSON input (supports single object, array, or stdin)
    /// </summary>
    private static async IAsyncEnumerable<IMessage> GenerateRequests(string? requestJson, MessageDescriptor inputType, bool verbose, bool allowUnknownFields)
    {
        // If data is from stdin (@), read JSON lines
        if (requestJson == "@")
        {
            AnsiConsole.MarkupLine("[dim]Reading request messages from stdin (one JSON object per line, Ctrl+D or empty line to finish):[/]");

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
                    AnsiConsole.MarkupLine($"[yellow]Warning:[/] Request contains {dynamicRequest.UnknownFields.Count} unknown field(s): {string.Join(", ", dynamicRequest.UnknownFields)}");
                }

                if (verbose)
                {
                    AnsiConsole.MarkupLine($"[dim]Sending: {line}[/]");
                }

                yield return request;
            }
        }
        else
        {
            // Single message mode - parse as JSON array or single object
            // Parse JSON outside the generator to avoid try-catch with yield
            if (requestJson is null)
            {
                yield break;
            }

            var jsonDoc = JsonDocument.Parse(requestJson);
            var isArray = jsonDoc.RootElement.ValueKind == JsonValueKind.Array;

            if (isArray)
            {
                // Multiple messages in an array
                foreach (var element in jsonDoc.RootElement.EnumerateArray())
                {
                    var elementJson = element.GetRawText();
                    var request = DynamicInvoker.CreateMessageFromJson(inputType, elementJson, allowUnknownFields);

                    // Log unknown fields if verbose mode is enabled
                    if (verbose && request is SimpleDynamicMessage { UnknownFields.Count: > 0 } dynamicRequest)
                    {
                        AnsiConsole.MarkupLine($"[yellow]Warning:[/] Request contains {dynamicRequest.UnknownFields.Count} unknown field(s): {string.Join(", ", dynamicRequest.UnknownFields)}");
                    }

                    if (verbose)
                    {
                        AnsiConsole.MarkupLine($"[dim]Sending: {elementJson}[/]");
                    }

                    yield return request;
                }
            }
            else
            {
                // Single message
                var request = DynamicInvoker.CreateMessageFromJson(inputType, requestJson, allowUnknownFields);

                // Log unknown fields if verbose mode is enabled
                if (verbose && request is SimpleDynamicMessage { UnknownFields.Count: > 0 } dynamicRequest)
                {
                    AnsiConsole.MarkupLine($"[yellow]Warning:[/] Request contains {dynamicRequest.UnknownFields.Count} unknown field(s): {string.Join(", ", dynamicRequest.UnknownFields)}");
                }

                if (verbose)
                {
                    AnsiConsole.MarkupLine($"[dim]Sending: {requestJson}[/]");
                }

                yield return request;
            }
        }
    }
}