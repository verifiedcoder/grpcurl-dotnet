using Google.Protobuf.Reflection;
using Grpc.Core;
using GrpCurl.Net.DescriptorSources;
using GrpCurl.Net.Exceptions;
using GrpCurl.Net.Utilities;
using Spectre.Console;
using System.CommandLine;
using System.Text.Json;

namespace GrpCurl.Net.Commands;

internal static class DescribeCommandHandler
{
    private static readonly JsonSerializerOptions JsonSerializerOptions = new()
    {
        WriteIndented = true
    };

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

        var connectTimeoutOpt = new Option<string?>("--connect-timeout")
        {
            Description = "Connection timeout (e.g., '10s', '1m', '500ms'). Default: 10s"
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
            Description = "User-Agent header value. Default: grpcurl-dotnet/1.0.0"
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

        var command = new Command("describe", "Describe a service or message")
        {
            addressArg,
            symbolArg,
            protosetOpt,
            plaintextOpt,
            insecureOpt,
            cacertOpt,
            certOpt,
            keyOpt,
            connectTimeoutOpt,
            authorityOpt,
            serverNameOpt,
            verboseOpt,
            veryVerboseOpt,
            userAgentOpt,
            headerOpt,
            reflectHeaderOpt,
            msgTemplateOpt,
            protosetOutOpt
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
            var connectTimeout = parseResult.GetValue(connectTimeoutOpt);
            var authority = parseResult.GetValue(authorityOpt);
            var serverName = parseResult.GetValue(serverNameOpt);
            var verbose = parseResult.GetValue(verboseOpt);
            var veryVerbose = parseResult.GetValue(veryVerboseOpt);
            var userAgent = parseResult.GetValue(userAgentOpt);
            var headers = parseResult.GetValue(headerOpt) ?? [];
            var reflectHeaders = parseResult.GetValue(reflectHeaderOpt) ?? [];
            var msgTemplate = parseResult.GetValue(msgTemplateOpt);
            var protosetOut = parseResult.GetValue(protosetOutOpt);

            await ExecuteAsync(
                address,
                symbol,
                protosets,
                plaintext,
                insecure,
                cacert,
                cert,
                key,
                connectTimeout,
                authority,
                serverName,
                verbose,
                veryVerbose,
                userAgent,
                headers,
                reflectHeaders,
                msgTemplate,
                protosetOut);
        });

        return command;
    }

    private static void ValidateOptions(string? address, string[] protosets, bool plaintext, bool insecure, string? serverName, bool verbose)
    {
        switch (protosets.Length)
        {
            // Validate required options
            case 0 when string.IsNullOrEmpty(address):

                AnsiConsole.MarkupLine("[red]Error:[/] Must specify either --protoset files or server address");
                AnsiConsole.MarkupLine("[dim]Examples:[/]");
                AnsiConsole.MarkupLine("[dim]  grpcurl-dotnet --protoset file.protoset describe[/]");
                AnsiConsole.MarkupLine("[dim]  grpcurl-dotnet localhost:9090 describe MyService[/]");

                throw new GrpcCommandException(CommandConstants.CommandFailed);

            // Warn about incompatible option combinations
            case > 0 when !string.IsNullOrEmpty(address):
                {
                    if (verbose)
                    {
                        AnsiConsole.MarkupLine("[yellow]Warning:[/] Both --protoset and address specified. Using protoset files (server reflection will be ignored).");
                    }

                    break;
                }
        }

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
    }

    private static async Task ExecuteAsync(
        string? address,
        string? symbol,
        string[] protosets,
        bool plaintext,
        bool insecure,
        string? cacert,
        string? cert,
        string? key,
        string? connectTimeout,
        string? authority,
        string? serverName,
        bool verbose,
        bool veryVerbose,
        string? userAgent,
        string[] headers,
        string[] reflectHeaders,
        bool msgTemplate,
        string? protosetOut)
    {
        var startTime = DateTime.UtcNow;

        // Validate options before proceeding
        ValidateOptions(address, protosets, plaintext, insecure, serverName, verbose);

        // Create timing context if very verbose mode is enabled
        var timing = veryVerbose ? new TimingContext() : null;

        try
        {
            IDescriptorSource descriptorSource;

            if (protosets.Length > 0)
            {
                if (verbose)
                {
                    AnsiConsole.MarkupLine($"[dim]Loading {protosets.Length} protoset file(s)...[/]");
                }

                timing?.StartPhase("Protoset Loading");

                descriptorSource = await ProtosetSource.LoadFromFilesAsync(protosets);

                if (verbose)
                {
                    AnsiConsole.MarkupLine("[dim]Protoset files loaded successfully[/]");
                }
            }
            else if (!string.IsNullOrEmpty(address))
            {
                if (verbose)
                {
                    AnsiConsole.MarkupLine($"[dim]Connecting to {address}...[/]");
                    AnsiConsole.MarkupLine($"[dim]Protocol: {(plaintext ? "HTTP/2 (plaintext)" : "HTTP/2 (TLS)")}[/]");

                    if (insecure)
                    {
                        AnsiConsole.MarkupLine("[dim]TLS verification: Disabled (--insecure)[/]");
                    }

                    if (connectTimeout is not null)
                    {
                        AnsiConsole.MarkupLine($"[dim]Connection timeout: {connectTimeout}[/]");
                    }

                    if (authority is not null)
                    {
                        AnsiConsole.MarkupLine($"[dim]Authority: {authority}[/]");
                    }
                }

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

                // Merge -H headers with --reflect-header
                var metadata = GrpcChannelFactory.CreateMetadata(
                    headers.Concat(reflectHeaders),
                    userAgent);

                descriptorSource = new ReflectionSource(channel, metadata, true);

                if (verbose)
                {
                    AnsiConsole.MarkupLine("[dim]Connected successfully, querying server reflection...[/]");
                }
            }
            else
            {
                // This should never happen due to ValidateOptions, but keep as fallback
                throw new InvalidOperationException("Must specify either --protoset or address");
            }

            timing?.StartPhase("Schema Discovery");

            if (string.IsNullOrEmpty(symbol))
            {
                if (verbose)
                {
                    AnsiConsole.MarkupLine("[dim]Describing all services...[/]");
                }

                var services = await descriptorSource.ListServicesAsync();

                if (verbose)
                {
                    AnsiConsole.MarkupLine($"[dim]Found {services.Count} service(s) to describe[/]");
                }

                foreach (var svc in services)
                {
                    await DescribeSymbolAsync(descriptorSource, svc, verbose, msgTemplate);

                    AnsiConsole.WriteLine();
                }

                // Export all services if --protoset-out specified
                if (!string.IsNullOrEmpty(protosetOut))
                {
                    await ProtosetExporter.WriteProtosetAsync(descriptorSource, protosetOut, [.. services]);

                    if (verbose)
                    {
                        AnsiConsole.MarkupLine($"[dim]Wrote protoset to {protosetOut}[/]");
                    }
                }
            }
            else
            {
                if (verbose)
                {
                    AnsiConsole.MarkupLine($"[dim]Describing symbol '{symbol}'...[/]");
                }

                await DescribeSymbolAsync(descriptorSource, symbol, verbose, msgTemplate);

                // Export the specific symbol if --protoset-out specified
                if (!string.IsNullOrEmpty(protosetOut))
                {
                    await ProtosetExporter.WriteProtosetAsync(descriptorSource, protosetOut, symbol);

                    if (verbose)
                    {
                        AnsiConsole.MarkupLine($"[dim]Wrote protoset to {protosetOut}[/]");
                    }
                }
            }

            // Print timing summary if very verbose mode
            timing?.PrintSummary();

            if (verbose)
            {
                var duration = DateTime.UtcNow - startTime;

                AnsiConsole.MarkupLine($"[dim]Operation completed in {duration.TotalMilliseconds:F0}ms[/]");
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
        catch (RpcException ex)
        {
            AnsiConsole.MarkupLine($"[red]gRPC Error:[/] {ex.Status.Detail}");
            AnsiConsole.MarkupLine($"[red]Status Code:[/] {ex.Status.StatusCode}");

            switch (ex.StatusCode)
            {
                case StatusCode.Unavailable:

                    AnsiConsole.MarkupLine($"[yellow]Connection failed to {address}[/]");
                    AnsiConsole.MarkupLine(CommandConstants.Suggestions);
                    AnsiConsole.MarkupLine("[dim]  - Ensure the server is running[/]");
                    AnsiConsole.MarkupLine("[dim]  - Try adding --plaintext if server doesn't use TLS[/]");
                    AnsiConsole.MarkupLine("[dim]  - Check firewall settings[/]");
                    AnsiConsole.MarkupLine("[dim]  - Verify the address and port are correct[/]");

                    break;

                case StatusCode.Unimplemented:

                    AnsiConsole.MarkupLine("[yellow]Server does not support reflection[/]");
                    AnsiConsole.MarkupLine(CommandConstants.Suggestions);
                    AnsiConsole.MarkupLine("[dim]  - Use --protoset to provide schema files instead[/]");
                    AnsiConsole.MarkupLine("[dim]  - Ask server admin to enable grpc-reflection[/]");

                    break;

                case StatusCode.NotFound:

                    AnsiConsole.MarkupLine($"[yellow]Symbol '{symbol}' not found on server[/]");
                    AnsiConsole.MarkupLine(CommandConstants.Suggestions);
                    AnsiConsole.MarkupLine("[dim]  - Use 'list' command to see available services[/]");
                    AnsiConsole.MarkupLine("[dim]  - Check the symbol name spelling and case[/]");
                    AnsiConsole.MarkupLine("[dim]  - Ensure the symbol is fully qualified (e.g., package.Service)[/]");

                    break;

                case StatusCode.OK:
                case StatusCode.Cancelled:
                case StatusCode.Unknown:
                case StatusCode.InvalidArgument:
                case StatusCode.DeadlineExceeded:
                case StatusCode.AlreadyExists:
                case StatusCode.PermissionDenied:
                case StatusCode.Unauthenticated:
                case StatusCode.ResourceExhausted:
                case StatusCode.FailedPrecondition:
                case StatusCode.Aborted:
                case StatusCode.OutOfRange:
                case StatusCode.Internal:
                case StatusCode.DataLoss:
                default:

                    throw new InvalidOperationException("Invalid Status Code.");
            }

            if (verbose)
            {
                Console.WriteLine(ex.StackTrace);
            }

            throw new GrpcCommandException(CommandConstants.CommandFailed);
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
    }

    private static async Task DescribeSymbolAsync(IDescriptorSource descriptorSource, string symbolName, bool verbose, bool msgTemplate)
    {
        var descriptor = await descriptorSource.FindSymbolAsync(symbolName);

        if (descriptor is null)
        {
            AnsiConsole.MarkupLine($"[red]Error:[/] Symbol '{symbolName}' not found");

            throw new GrpcCommandException(CommandConstants.CommandFailed);
        }

        if (verbose)
        {
            var descriptorType = descriptor switch
            {
                ServiceDescriptor => "Service",
                MessageDescriptor => "Message",
                EnumDescriptor => "Enum",
                _ => "Symbol"
            };

            AnsiConsole.MarkupLine($"[dim]Found {descriptorType}: {descriptor.FullName}[/]");
        }

        // If --msg-template is specified, generate JSON template for message types
        if (msgTemplate && descriptor is MessageDescriptor messageDescriptor)
        {
            var template = CreateMessageTemplate(messageDescriptor, []);

            Console.WriteLine(JsonSerializer.Serialize(template, JsonSerializerOptions));

            return;
        }

        var description = descriptor switch
        {
            ServiceDescriptor svc => $"Service: {svc.Name}\nMethods: {svc.Methods.Count}",
            MessageDescriptor msg => $"Message: {msg.Name}\nFields: {msg.Fields.InDeclarationOrder().Count}",
            EnumDescriptor enm => $"Enum: {enm.Name}\nValues: {enm.Values.Count}",
            _ => "Unknown type"
        };

        var panel = new Panel(description).Header($"[bold cyan]{descriptor.FullName}[/]").Border(BoxBorder.Rounded);

        AnsiConsole.Write(panel);
    }

    /// <summary>
    ///     Creates a JSON template for a message descriptor with recursion detection.
    /// </summary>
    /// <param name="messageDescriptor">The message descriptor to create a template for</param>
    /// <param name="visitedTypes">Set of visited type full names to detect recursion</param>
    /// <returns>Dictionary representing the JSON template</returns>
    private static Dictionary<string, object?> CreateMessageTemplate(MessageDescriptor messageDescriptor, HashSet<string> visitedTypes)
    {
        var template = new Dictionary<string, object?>();

        // Check for recursion
        if (visitedTypes.Contains(messageDescriptor.FullName))
        {
            // Return a placeholder for recursive types
            template["<recursive>"] = messageDescriptor.FullName;

            return template;
        }

        // Add current type to visited set
        var currentVisited = new HashSet<string>(visitedTypes) { messageDescriptor.FullName };

        foreach (var field in messageDescriptor.Fields.InDeclarationOrder())
        {
            template[field.JsonName] = GetDefaultValueForField(field, currentVisited);
        }

        return template;
    }

    /// <summary>
    ///     Gets the default template value for a field based on its type.
    /// </summary>
    private static object? GetDefaultValueForField(FieldDescriptor field, HashSet<string> visitedTypes)
    {
        // Handle repeated fields (arrays)
        if (!field.IsRepeated)
        {
            return field.FieldType switch
            {
                FieldType.Message => HandleWellKnownType(field.MessageType, visitedTypes),
                FieldType.Enum => GetEnumDefault(field.EnumType),
                _ => GetScalarDefault(field)
            };
        }

        // Handle non-repeated fields
        // For map fields
        if (field.IsMap)
        {
            var mapTemplate = new Dictionary<string, object?>();
            var mapValueField = field.MessageType.Fields[2]; // Value field in map entry

            mapTemplate["<key>"] = GetScalarDefault(mapValueField);

            return mapTemplate;
        }

        // For regular repeated fields
        var arrayTemplate = new List<object?>();
        var elementValue = field.FieldType switch
        {
            FieldType.Message => CreateMessageTemplate(field.MessageType, visitedTypes),
            FieldType.Enum => GetEnumDefault(field.EnumType),
            _ => GetScalarDefault(field)
        };

        arrayTemplate.Add(elementValue);

        return arrayTemplate;
    }

    /// <summary>
    ///     Handles well-known types with special formatting.
    /// </summary>
    private static object HandleWellKnownType(MessageDescriptor messageDescriptor, HashSet<string> visitedTypes)
    {
        // Check for well-known types and provide appropriate defaults
        return messageDescriptor.FullName switch
        {
            "google.protobuf.Timestamp" => "1970-01-01T00:00:00Z",
            "google.protobuf.Duration" => "0s",
            "google.protobuf.Int32Value" => 0,
            "google.protobuf.Int64Value" => 0,
            "google.protobuf.UInt32Value" => 0,
            "google.protobuf.UInt64Value" => 0,
            "google.protobuf.FloatValue" => 0.0,
            "google.protobuf.DoubleValue" => 0.0,
            "google.protobuf.BoolValue" => false,
            "google.protobuf.StringValue" => "",
            "google.protobuf.BytesValue" => "",
            "google.protobuf.Empty" => new Dictionary<string, object?>(),
            _ => CreateMessageTemplate(messageDescriptor, visitedTypes)
        };
    }

    /// <summary>
    ///     Gets the default value for an enum field.
    /// </summary>
    /// <remarks>Return the first enum value name (usually the zero value).</remarks>
    private static string GetEnumDefault(EnumDescriptor enumDescriptor)
        => enumDescriptor.Values.Count > 0 ? enumDescriptor.Values[0].Name : "UNKNOWN";

    /// <summary>
    ///     Gets the default value for a scalar field.
    /// </summary>
    private static object? GetScalarDefault(FieldDescriptor field)
        => field.FieldType switch
        {
            FieldType.Double => 0.0,
            FieldType.Float => 0.0,
            FieldType.Int32 => 0,
            FieldType.Int64 => 0,
            FieldType.UInt32 => 0,
            FieldType.UInt64 => 0,
            FieldType.SInt32 => 0,
            FieldType.SInt64 => 0,
            FieldType.Fixed32 => 0,
            FieldType.Fixed64 => 0,
            FieldType.SFixed32 => 0,
            FieldType.SFixed64 => 0,
            FieldType.Bool => false,
            FieldType.String => "",
            FieldType.Bytes => "",
            _ => null
        };
}