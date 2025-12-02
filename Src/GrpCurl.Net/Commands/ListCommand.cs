using Google.Protobuf.Reflection;
using Grpc.Core;
using GrpCurl.Net.DescriptorSources;
using GrpCurl.Net.Exceptions;
using GrpCurl.Net.Utilities;
using Spectre.Console;
using System.CommandLine;

namespace GrpCurl.Net.Commands;

internal static class ListCommandHandler
{
    public static Command Create()
    {
        var addressArg = new Argument<string?>("address")
        {
            Description = "Server address (host:port)",
            Arity = ArgumentArity.ZeroOrOne
        };

        var serviceArg = new Argument<string?>("service")
        {
            Description = "Service name to list methods for",
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

        var protosetOutOpt = new Option<string?>("--protoset-out")
        {
            Description = "Write FileDescriptorSet to file after operation"
        };

        var command = new Command("list", "List services or methods")
        {
            addressArg,
            serviceArg,
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
            protosetOutOpt
        };

        // Use ParseResult to handle parameters
        command.SetAction(async (parseResult, _) =>
        {
            var address = parseResult.GetValue(addressArg);
            var service = parseResult.GetValue(serviceArg);
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
            var protosetOut = parseResult.GetValue(protosetOutOpt);

            await ExecuteAsync(
                address,
                service,
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
                AnsiConsole.MarkupLine("[dim]  grpcurl-dotnet --protoset file.protoset list[/]");
                AnsiConsole.MarkupLine("[dim]  grpcurl-dotnet localhost:9090 list[/]");

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
        string? service,
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
                throw new InvalidOperationException("Must specify either --protoset files or server address");
            }

            timing?.StartPhase("Schema Discovery");

            if (string.IsNullOrEmpty(service))
            {
                await ListServicesAsync(descriptorSource, verbose);

                // Export all services if --protoset-out specified
                if (!string.IsNullOrEmpty(protosetOut))
                {
                    var services = await descriptorSource.ListServicesAsync();

                    await ProtosetExporter.WriteProtosetAsync(descriptorSource, protosetOut, [.. services]);

                    if (verbose)
                    {
                        AnsiConsole.MarkupLine($"[dim]Wrote protoset to {protosetOut}[/]");
                    }
                }
            }
            else
            {
                await ListMethodsAsync(descriptorSource, service, verbose);

                // Export the specific service if --protoset-out specified
                if (!string.IsNullOrEmpty(protosetOut))
                {
                    await ProtosetExporter.WriteProtosetAsync(descriptorSource, protosetOut, service);

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

                case StatusCode.OK:
                case StatusCode.Cancelled:
                case StatusCode.Unknown:
                case StatusCode.InvalidArgument:
                case StatusCode.DeadlineExceeded:
                case StatusCode.NotFound:
                case StatusCode.AlreadyExists:
                case StatusCode.PermissionDenied:
                case StatusCode.Unauthenticated:
                case StatusCode.ResourceExhausted:
                case StatusCode.FailedPrecondition:
                case StatusCode.Aborted:
                case StatusCode.OutOfRange:
                case StatusCode.Internal:
                case StatusCode.DataLoss:

                    break;

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

            if (!verbose && ex.InnerException is null)
            {
                throw new GrpcCommandException(CommandConstants.CommandFailed);
            }

            if (ex.InnerException is not null)
            {
                AnsiConsole.MarkupLine($"[dim]{ex.InnerException.Message}[/]");
            }

            if (verbose)
            {
                Console.WriteLine(ex.StackTrace);
            }

            throw new GrpcCommandException(CommandConstants.CommandFailed);
        }
    }

    private static async Task ListServicesAsync(IDescriptorSource descriptorSource, bool verbose)
    {
        if (verbose)
        {
            AnsiConsole.MarkupLine("[dim]Listing services...[/]");
        }

        var services = await descriptorSource.ListServicesAsync();

        if (services.Count == 0)
        {
            AnsiConsole.MarkupLine("[yellow](No services)[/]");

            return;
        }

        var table = new Table()
            .Border(TableBorder.Rounded)
            .AddColumn("Service");

        foreach (var svc in services)
        {
            table.AddRow($"[cyan]{svc}[/]");
        }

        AnsiConsole.Write(table);
        AnsiConsole.WriteLine($"\nTotal: {services.Count} service(s)");
    }

    private static async Task ListMethodsAsync(IDescriptorSource descriptorSource, string serviceName, bool verbose)
    {
        if (verbose)
        {
            AnsiConsole.MarkupLine($"[dim]Finding service '{serviceName}'...[/]");
        }

        var descriptor = await descriptorSource.FindSymbolAsync(serviceName);

        if (descriptor is not ServiceDescriptor serviceDescriptor)
        {
            AnsiConsole.MarkupLine($"[red]Error:[/] '{serviceName}' is not a service");
            throw new GrpcCommandException(CommandConstants.CommandFailed);
        }

        if (verbose)
        {
            AnsiConsole.MarkupLine($"[dim]Found service with {serviceDescriptor.Methods.Count} method(s)[/]");
        }

        if (serviceDescriptor.Methods.Count == 0)
        {
            AnsiConsole.MarkupLine("[yellow](No methods)[/]");

            return;
        }

        var table = new Table()
            .Border(TableBorder.Rounded)
            .AddColumn("Method")
            .AddColumn("Type")
            .AddColumn("Input")
            .AddColumn("Output");

        foreach (var method in serviceDescriptor.Methods)
        {
            var methodType = GetMethodType(method);

            table.AddRow(
                $"[cyan]{method.Name}[/]",
                $"[yellow]{methodType}[/]",
                $"[dim]{method.InputType.FullName}[/]",
                $"[dim]{method.OutputType.FullName}[/]"
            );
        }

        AnsiConsole.Write(table);
        AnsiConsole.WriteLine($"\nTotal: {serviceDescriptor.Methods.Count} method(s)");
    }

    private static string GetMethodType(MethodDescriptor method)
    {
        if (method is { IsClientStreaming: true, IsServerStreaming: true })
        {
            return "Bidi Streaming";
        }

        if (method.IsClientStreaming)
        {
            return "Client Streaming";
        }

        return method.IsServerStreaming ? "Server Streaming" : "Unary";
    }
}