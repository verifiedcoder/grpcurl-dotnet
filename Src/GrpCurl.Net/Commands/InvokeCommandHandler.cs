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

        var unsafeShowSecretsOpt = new Option<bool>("--unsafe-show-secrets")
        {
            Description = "Opt out of secret redaction in verbose output. Sensitive headers " +
                          "(authorization, cookie, *-token, *-secret, *-bin, etc.) are " +
                          "redacted by default to keep CI logs and terminal captures safe."
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
                          "Default is EphemeralKeySet (keys never persist to the key store)."
        };

        var keepaliveTimeOpt = new Option<string?>("--keepalive-time")
        {
            Description = "HTTP/2 keepalive ping interval (e.g., '30s'). Default: 60s. " +
                          "Use 'infinite' to disable keepalive pings."
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

        var requestFormatOpt = new Option<string?>("--format")
        {
            Description = "Request data format: 'json' (default) or 'text' (protobuf text format)."
        };

        var protoOutDirOpt = new Option<string?>("--proto-out-dir")
        {
            Description = "Reconstruct .proto source files from the active schema and write them to this directory. " +
                          "Refuses to overwrite without --force."
        };

        var maxStdinBytesOpt = new Option<long?>("--max-stdin-bytes")
        {
            Description = "Maximum bytes accepted from stdin (with --data @). Default: 16 MiB. " +
                          "Rejects oversized inputs before parsing."
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
            forceOpt,
            unsafeShowSecretsOpt,
            revocationModeOpt,
            exportableKeyOpt,
            keepaliveTimeOpt,
            keepaliveTimeoutOpt,
            protoOpt,
            importPathOpt,
            requestFormatOpt,
            protoOutDirOpt,
            maxStdinBytesOpt
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
            var unsafeShowSecrets = parseResult.GetValue(unsafeShowSecretsOpt);
            var revocationMode = parseResult.GetValue(revocationModeOpt);
            var exportableKey = parseResult.GetValue(exportableKeyOpt);
            var keepaliveTime = parseResult.GetValue(keepaliveTimeOpt);
            var keepaliveTimeout = parseResult.GetValue(keepaliveTimeoutOpt);
            var protoFiles = parseResult.GetValue(protoOpt);
            var importPaths = parseResult.GetValue(importPathOpt);
            var requestFormat = parseResult.GetValue(requestFormatOpt);
            var protoOutDir = parseResult.GetValue(protoOutDirOpt);

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
                    force,
                    unsafeShowSecrets,
                    revocationMode,
                    exportableKey,
                    keepaliveTime,
                    keepaliveTimeout,
                    protoFiles,
                    importPaths,
                    requestFormat,
                    protoOutDir);

                return 0;
            }
            catch (GrpcCommandException ex)
            {
                return ex.ExitCode;
            }
        });

        return command;
    }

    internal static IMessage ParseRequestPayload(
        Google.Protobuf.Reflection.MessageDescriptor inputType,
        string? requestText,
        bool allowUnknownFields,
        bool textFormat)
    {
        if (textFormat)
        {
            if (string.IsNullOrEmpty(requestText))
            {
                return new SimpleDynamicMessage(inputType);
            }

            return DynamicTextFormat.Parse(inputType, requestText);
        }

        return DynamicInvoker.CreateMessageFromJson(inputType, requestText, allowUnknownFields);
    }

    internal static System.Security.Cryptography.X509Certificates.X509RevocationMode? ParseRevocationMode(string? mode)
    {
        if (string.IsNullOrEmpty(mode))
        {
            return null;
        }

        return mode.ToLowerInvariant() switch
        {
            "online" => System.Security.Cryptography.X509Certificates.X509RevocationMode.Online,
            "offline" => System.Security.Cryptography.X509Certificates.X509RevocationMode.Offline,
            "nocheck" or "no-check" or "none" => System.Security.Cryptography.X509Certificates.X509RevocationMode.NoCheck,
            _ => throw new ArgumentException(
                $"Unknown --revocation-mode '{mode}'. Expected: online, offline, nocheck.",
                nameof(mode))
        };
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
        bool force = false,
        bool unsafeShowSecrets = false,
        string? revocationMode = null,
        bool exportableKey = false,
        string? keepaliveTime = null,
        string? keepaliveTimeout = null,
        string[]? protoFiles = null,
        string[]? importPaths = null,
        string? requestFormat = null,
        string? protoOutDir = null)
    {
        // Validate options before proceeding
        ValidateOptions(plaintext, insecure, serverName, maxMsgSz, verbose);

        var parsedRevocationMode = ParseRevocationMode(revocationMode);
        var parsedKeepaliveTime = keepaliveTime is null ? (TimeSpan?)null : GrpcChannelFactory.ParseDuration(keepaliveTime);
        var parsedKeepaliveTimeout = keepaliveTimeout is null ? (TimeSpan?)null : GrpcChannelFactory.ParseDuration(keepaliveTimeout);
        var useTextFormat = !string.IsNullOrEmpty(requestFormat)
            && string.Equals(requestFormat, "text", StringComparison.OrdinalIgnoreCase);

        if (!string.IsNullOrEmpty(requestFormat)
            && !string.Equals(requestFormat, "json", StringComparison.OrdinalIgnoreCase)
            && !useTextFormat)
        {
            ErrorRenderer.RenderAndThrow(new ErrorEnvelope
            {
                Category = ErrorCategory.Usage,
                ExitCode = 2,
                Message = $"Unknown --format '{requestFormat}'. Expected: json, text."
            }, output);
        }

        // Create timing context if very verbose mode is enabled
        var timing = veryVerbose ? new TimingContext() : null;

        // --max-time bounds the *entire* operation, not just the RPC. The deadline starts
        // here so it covers protoset loading, reflection schema lookup, stdin reads,
        // mapping/variables file reads, and the actual RPC. Previously this was only
        // wired in around line 476 (i.e. after schema and stdin), which let slow probes
        // outlive the budget. See CODE-REVIEW.md P1 "--max-time is not a total operation
        // deadline".
        var maxTimeSpan = maxTime is not null ? GrpcChannelFactory.ParseDuration(maxTime) : (TimeSpan?)null;

        using var ctrlCCts = new CancellationTokenSource();
        using var deadlineCts = maxTimeSpan is not null
            ? new CancellationTokenSource(maxTimeSpan.Value)
            : new CancellationTokenSource();
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(deadlineCts.Token, ctrlCCts.Token);

        var operationToken = linkedCts.Token;
        DateTime? rpcDeadline = maxTimeSpan is not null ? DateTime.UtcNow.Add(maxTimeSpan.Value) : null;

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

            // Build ONE connection options bundle that applies to both reflection and the RPC.
            // The earlier version built a second, half-populated options object for the RPC
            // channel that silently dropped CaCertPath/ClientCertPath/ClientKeyPath/
            // ClientCertPassword. That defeated mTLS for the actual business call even when
            // the reflection probe succeeded. See CODE-REVIEW.md P0 "invoke drops TLS/mTLS".
            var connectionOptions = new GrpcChannelFactory.ChannelOptions
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
                ServerName = serverName,
                RevocationMode = parsedRevocationMode,
                ExportableClientKey = exportableKey,
                KeepaliveTime = parsedKeepaliveTime,
                KeepaliveTimeout = parsedKeepaliveTimeout
            };

            // Reflection metadata is merged from -H plus --reflect-header so callers can target
            // proxy vs origin separately if needed. Compute it up-front because DescriptorSourceFactory
            // owns the only channel from this point on.
            var reflectionMetadata = GrpcChannelFactory.CreateMetadata(
                (headerStrings ?? []).Concat(reflectHeaders ?? []),
                userAgent);

            timing?.StartPhase(protosets is { Length: > 0 } ? "Protoset Loading" : "Connection Establishment");

            await using var session = await DescriptorSourceFactory.CreateAsync(
                address,
                protosets ?? [],
                protoFiles ?? [],
                importPaths ?? [],
                connectionOptions,
                reflectionMetadata,
                operationToken);

            var descriptorSource = session.Source;

            timing?.StartPhase("Schema Discovery");

            var serviceDescriptor = await descriptorSource.FindSymbolAsync(serviceName, operationToken);

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

                requestJson = await reader.ReadToEndAsync(operationToken);
            }
            else
            {
                requestJson = data;
            }

            timing?.StartPhase("RPC Channel Setup");

            // The RPC reuses the same channel that backs reflection. This is mandatory for
            // mTLS to work end-to-end and removes a class of "reflection works but the RPC
            // gets WhoAmI=anonymous" failures. The old code created channelOptions2 here
            // that omitted the TLS material — that block is deliberately removed.
            //
            // session.Channel is only null when the descriptor source was loaded purely
            // from protoset files with no address. Invoke always supplies an address
            // (it has to — it's calling a method), so the assertion is upheld in practice.
            // If a future path ever lets this be null, the message tells the operator
            // exactly which option combination produced it.
            var rpcChannel = session.Channel ?? throw new InvalidOperationException(
                "Invoke requires a network channel; supply a host:port address.");

            var invoker = new DynamicInvoker(rpcChannel);

            // Create RPC metadata by merging -H headers with --rpc-header
            var metadata = GrpcChannelFactory.CreateMetadata(
                (headerStrings ?? []).Concat(rpcHeaders ?? []),
                userAgent);

            // operationToken / rpcDeadline are computed at the top of ExecuteAsync so the
            // budget covers protoset loading, reflection, stdin reads, and the RPC. The
            // remaining deadline for the gRPC call is derived from "wall-clock now" so a
            // slow descriptor probe shrinks the budget the RPC sees.
            var cancellationToken = operationToken;
            var deadline = rpcDeadline;

            timing?.StartPhase("RPC Invocation");

            switch (methodDescriptor.IsClientStreaming)
            {
                case false when !methodDescriptor.IsServerStreaming:

                    await InvokeUnaryAsync(invoker, methodDescriptor, requestJson, metadata, verbose, emitDefaults, allowUnknownFields, deadline, timing, output, unsafeShowSecrets, useTextFormat, cancellationToken);

                    break;

                case false when methodDescriptor.IsServerStreaming:

                    await InvokeServerStreamingAsync(invoker, methodDescriptor, requestJson, metadata, verbose, emitDefaults, allowUnknownFields, deadline, timing, output, unsafeShowSecrets, useTextFormat, cancellationToken);

                    break;

                case true when !methodDescriptor.IsServerStreaming:

                    await InvokeClientStreamingAsync(invoker, methodDescriptor, requestJson, metadata, verbose, emitDefaults, allowUnknownFields, deadline, timing, output, unsafeShowSecrets, useTextFormat, cancellationToken);

                    break;

                default:

                    // Bidirectional streaming
                    await InvokeBidirectionalStreamingAsync(invoker, methodDescriptor, requestJson, metadata, verbose, emitDefaults, allowUnknownFields, deadline, timing, output, unsafeShowSecrets, useTextFormat, cancellationToken);

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

            // Export reconstructed .proto sources if --proto-out-dir specified.
            if (!string.IsNullOrEmpty(protoOutDir))
            {
                await GrpCurl.Net.Output.ProtoFileEmitter.WriteAsync(descriptorSource, protoOutDir, force, operationToken);

                if (verbose)
                {
                    Diagnostics.Markup($"[dim]Wrote .proto files to {protoOutDir}[/]");
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
            else if (maxTime is not null && deadlineCts.IsCancellationRequested)
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
            // Decode grpc-status-details-bin (a base64-encoded google.rpc.Status with
            // typed Any payloads). Surfaces structured details in the JSON envelope and
            // human-readable hints in text mode. See CODE-REVIEW.md P2 "Metadata and
            // Error Parity Is Incomplete".
            var decodedDetails = RichStatusDecoder.TryDecode(ex);

            RpcStatusDetailsInfo? statusDetailsInfo = null;

            if (decodedDetails is not null)
            {
                var entries = new List<RpcStatusDetailEntry>(decodedDetails.Details.Count);

                foreach (var detail in decodedDetails.Details)
                {
                    entries.Add(new RpcStatusDetailEntry
                    {
                        TypeUrl = detail.TypeUrl,
                        RawBase64 = detail.ParsedMessage is null ? Convert.ToBase64String(detail.RawValue) : null,
                        Json = detail.ParsedMessage is { } parsed
                            ? Google.Protobuf.JsonFormatter.Default.Format(parsed)
                            : null
                    });
                }

                statusDetailsInfo = new RpcStatusDetailsInfo
                {
                    Code = decodedDetails.Code,
                    Message = decodedDetails.Message,
                    Details = entries
                };
            }

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
                    Detail = ex.Status.Detail,
                    StatusDetails = statusDetailsInfo
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
        bool unsafeShowSecrets,
        bool useTextFormat,
        CancellationToken cancellationToken)
    {
        timing?.StartPhase(CommandConstants.RequestSerialisation);

        var request = ParseRequestPayload(methodDescriptor.InputType, requestJson, allowUnknownFields, useTextFormat);

        // Log unknown fields if verbose mode is enabled
        if (verbose && request is SimpleDynamicMessage { UnknownFields.Count: > 0 } dynamicRequest)
        {
            Diagnostics.Markup($"[yellow]Warning:[/] Request contains {dynamicRequest.UnknownFields.Count} unknown field(s): {Markup.Escape(string.Join(", ", dynamicRequest.UnknownFields))}");
        }

        if (verbose)
        {
            WriteVerboseMethodInfo(methodDescriptor, metadata, unsafeShowSecrets);
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

        OutputRenderer.WriteInvokeMessage(result.Response, index: 0, emitDefaults, output, textFormat: useTextFormat);

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
        bool unsafeShowSecrets,
        bool useTextFormat,
        CancellationToken cancellationToken)
    {
        timing?.StartPhase(CommandConstants.RequestSerialisation);

        var request = ParseRequestPayload(methodDescriptor.InputType, requestJson, allowUnknownFields, useTextFormat);

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

        using var streamingResult = invoker.InvokeServerStreamingWithMetadataAsync(methodDescriptor, request, metadata, deadline, cancellationToken);

        if (verbose)
        {
            // Headers may not be available until the first response arrives, but we have
            // a Task and can await it without forcing a yield. WhenAny lets us print
            // headers as soon as the server flushes them.
            var headers = await streamingResult.ResponseHeadersAsync;

            WriteVerboseResponseHeaders(headers);
            await Console.Error.WriteLineAsync("Response stream:");
        }

        await foreach (var response in streamingResult.ResponseStream)
        {
            if (responseCount == 0)
            {
                timing?.StartPhase(CommandConstants.ResponseDeserialization);
            }

            OutputRenderer.WriteInvokeMessage(response, responseCount, emitDefaults, output, textFormat: useTextFormat);

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
            WriteVerboseResponseTrailers(streamingResult.GetTrailers());
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
        bool unsafeShowSecrets,
        bool useTextFormat,
        CancellationToken cancellationToken)
    {
        if (verbose)
        {
            Diagnostics.Markup("[dim]Starting client streaming...[/]");
        }

        timing?.StartPhase(CommandConstants.RequestSerialisation);

        var sentCount = 0;
        long totalRequestSize = 0;

        using var clientResult = await invoker.InvokeClientStreamingWithMetadataAsync(methodDescriptor, TrackRequests(), metadata, deadline, cancellationToken);

        timing?.StartPhase(CommandConstants.ResponseDeserialization);

        if (timing is not null)
        {
            timing.RequestSizeBytes = totalRequestSize;
            timing.ResponseSizeBytes = clientResult.Response.CalculateSize();
            timing.MessageCount = sentCount;
        }

        if (verbose)
        {
            WriteVerboseResponseHeaders(await clientResult.ResponseHeadersAsync);
            await Console.Error.WriteLineAsync("Response contents:");
        }

        OutputRenderer.WriteInvokeMessage(clientResult.Response, index: 0, emitDefaults, output, textFormat: useTextFormat);

        if (verbose)
        {
            WriteVerboseResponseTrailers(clientResult.GetTrailers());
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
        bool unsafeShowSecrets,
        bool useTextFormat,
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

        using var duplexResult = invoker.InvokeDuplexStreamingWithMetadataAsync(methodDescriptor, TrackRequests(), metadata, deadline, cancellationToken);

        if (verbose)
        {
            WriteVerboseResponseHeaders(await duplexResult.ResponseHeadersAsync);
            await Console.Error.WriteLineAsync("Response stream:");
        }

        await foreach (var response in duplexResult.ResponseStream)
        {
            if (responseCount == 0)
            {
                timing?.StartPhase(CommandConstants.ResponseDeserialization);
            }

            OutputRenderer.WriteInvokeMessage(response, responseCount, emitDefaults, output, textFormat: useTextFormat);

            responseCount++;
            totalResponseSize += response.CalculateSize();
        }

        if (verbose)
        {
            WriteVerboseResponseTrailers(duplexResult.GetTrailers());
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

    internal static void WriteVerboseMethodInfo(MethodDescriptor methodDescriptor, Metadata metadata, bool unsafeShowSecrets = false)
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
            // Sensitive values (authorization, cookie, *-token, *-secret, *-bin, etc.)
            // are redacted by default so CI logs and terminal captures stay safe.
            // Use --unsafe-show-secrets to see raw values.
            foreach (var line in SecretRedactor.FormatLines(metadata, unsafeShowSecrets))
            {
                Console.Error.WriteLine(line);
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
    ///     Generates request messages from JSON input (supports single object, array,
    ///     concatenated objects, or stdin in any of those forms). When stdin is used,
    ///     the body is read fully up to <paramref name="maxStdinBytes"/> bytes (default
    ///     16 MiB) so we can accept pretty-printed JSON arrays and concatenated objects
    ///     across multiple lines — parity with upstream grpcurl. The earlier line-based
    ///     stdin parser broke on blank lines (CODE-REVIEW.md P2 "Streaming stdin
    ///     less capable than inline JSON").
    /// </summary>
    private static async IAsyncEnumerable<IMessage> GenerateRequests(
        string? requestJson,
        MessageDescriptor inputType,
        bool verbose,
        bool allowUnknownFields,
        long maxStdinBytes = 16L * 1024 * 1024)
    {
        if (requestJson is null)
        {
            yield break;
        }

        string body;

        if (requestJson == "@")
        {
            if (verbose)
            {
                Diagnostics.Markup("[dim]Reading request messages from stdin (JSON array, concatenated objects, or pretty-printed JSON until EOF)...[/]");
            }

            body = await ReadStdinBoundedAsync(maxStdinBytes);
        }
        else
        {
            body = requestJson;
        }

        if (string.IsNullOrWhiteSpace(body))
        {
            yield break;
        }

        // Single-message mode: parse as JSON array, single object, or concatenated objects.
        List<string>? messages;

        try
        {
            var jsonDoc = JsonDocument.Parse(body);
            var isArray = jsonDoc.RootElement.ValueKind == JsonValueKind.Array;

            if (isArray)
            {
                messages = [];
                messages.AddRange(jsonDoc.RootElement.EnumerateArray().Select(element => element.GetRawText()));
            }
            else
            {
                messages = [body];
            }
        }
        catch (JsonException)
        {
            // Standard parse failed -- try concatenated JSON objects (Go grpcurl format: {...} {...})
            messages = ParseConcatenatedJsonObjects(body);
        }

        foreach (var msgJson in messages)
        {
            var request = DynamicInvoker.CreateMessageFromJson(inputType, msgJson, allowUnknownFields);

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

    /// <summary>
    ///     Reads stdin into a string, refusing inputs larger than
    ///     <paramref name="maxBytes"/> so a wedged or hostile producer can't drive the
    ///     CLI into memory pressure. Returns immediately on EOF.
    /// </summary>
    private static async Task<string> ReadStdinBoundedAsync(long maxBytes)
    {
        await using var stdin = Console.OpenStandardInput();
        await using var buffer = new MemoryStream();

        var read = new byte[8192];
        long total = 0;
        int n;

        while ((n = await stdin.ReadAsync(read.AsMemory(0, read.Length))) > 0)
        {
            total += n;

            if (total > maxBytes)
            {
                throw new InvalidOperationException(
                    $"Stdin exceeded the maximum allowed size of {maxBytes:N0} bytes. " +
                    "Increase --max-stdin-bytes or split the payload.");
            }

            await buffer.WriteAsync(read.AsMemory(0, n));
        }

        return System.Text.Encoding.UTF8.GetString(buffer.GetBuffer(), 0, (int)buffer.Length);
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