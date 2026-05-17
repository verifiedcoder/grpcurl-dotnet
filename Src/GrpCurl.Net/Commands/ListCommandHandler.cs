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

        var certPasswordOpt = new Option<string?>("--cert-password")
        {
            Description = "Password for PKCS12 (.p12/.pfx) client certificate"
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

        var outputOpt = OutputFormatOption.Build();

        var forceOpt = new Option<bool>("--force")
        {
            Description = "Overwrite existing files (e.g., target of --protoset-out) without confirmation"
        };

        var command = new Command("list", CommandDescriptions.List)
        {
            addressArg,
            serviceArg,
            protosetOpt,
            plaintextOpt,
            insecureOpt,
            cacertOpt,
            certOpt,
            keyOpt,
            certPasswordOpt,
            connectTimeoutOpt,
            authorityOpt,
            serverNameOpt,
            verboseOpt,
            veryVerboseOpt,
            userAgentOpt,
            headerOpt,
            reflectHeaderOpt,
            protosetOutOpt,
            outputOpt,
            forceOpt
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
            var certPassword = parseResult.GetValue(certPasswordOpt);
            var connectTimeout = parseResult.GetValue(connectTimeoutOpt);
            var authority = parseResult.GetValue(authorityOpt);
            var serverName = parseResult.GetValue(serverNameOpt);
            var verbose = parseResult.GetValue(verboseOpt);
            var veryVerbose = parseResult.GetValue(veryVerboseOpt);
            var userAgent = parseResult.GetValue(userAgentOpt);
            var headers = parseResult.GetValue(headerOpt) ?? [];
            var reflectHeaders = parseResult.GetValue(reflectHeaderOpt) ?? [];
            var protosetOut = parseResult.GetValue(protosetOutOpt);
            var output = parseResult.GetValue(outputOpt);
            var force = parseResult.GetValue(forceOpt);

            try
            {
                await ExecuteAsync(
                    address,
                    service,
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

    internal static void ValidateOptions(string? address, string[] protosets, bool plaintext, bool insecure, string? serverName, bool verbose, OutputFormat output = OutputFormat.Text)
    {
        switch (protosets.Length)
        {
            // Validate required options
            case 0 when string.IsNullOrEmpty(address):

                ErrorRenderer.RenderAndThrow(new ErrorEnvelope
                {
                    Category = ErrorCategory.Usage,
                    ExitCode = 2,
                    Message = "Must specify either --protoset files or server address",
                    Suggestions =
                    [
                        "grpcurl-dotnet --protoset file.protoset list",
                        "grpcurl-dotnet localhost:9090 list"
                    ]
                }, output);

                break;

            // Warn about incompatible option combinations
            case > 0 when !string.IsNullOrEmpty(address):
                {
                    if (verbose)
                    {
                        Diagnostics.Markup("[yellow]Warning:[/] Both --protoset and address specified. Using protoset files (server reflection will be ignored).");
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
        string? service,
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
        string? protosetOut,
        OutputFormat output = OutputFormat.Text,
        bool force = false)
    {
        var startTime = DateTime.UtcNow;

        // When using protosets without a server, the first positional arg (address) is actually the service
        if (protosets.Length > 0 && !string.IsNullOrEmpty(address) && string.IsNullOrEmpty(service))
        {
            service = address;
            address = null;
        }

        // Validate options before proceeding
        ValidateOptions(address, protosets, plaintext, insecure, serverName, verbose, output);

        // Create timing context if very verbose mode is enabled
        var timing = veryVerbose ? new TimingContext() : null;

        // Single ConnectionOptions bundle covers reflection. List has no business RPC, but
        // we still build it through DescriptorSourceFactory so that channel lifetime and
        // TLS material are handled the same way as invoke/describe/Gql2Grpc.
        var channelOptions = new GrpcChannelFactory.ChannelOptions
        {
            Plaintext = plaintext,
            InsecureSkipVerify = insecure,
            CaCertPath = cacert,
            ClientCertPath = cert,
            ClientKeyPath = key,
            ClientCertPassword = certPassword,
            ConnectTimeout = connectTimeout is not null ? GrpcChannelFactory.ParseDuration(connectTimeout) : null,
            Authority = authority,
            ServerName = serverName
        };

        var reflectionMetadata = GrpcChannelFactory.CreateMetadata(
            headers.Concat(reflectHeaders),
            userAgent);

        if (verbose && !string.IsNullOrEmpty(address) && protosets.Length == 0)
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

            if (authority is not null)
            {
                Diagnostics.Markup($"[dim]Authority: {authority}[/]");
            }
        }

        try
        {
            timing?.StartPhase(protosets.Length > 0 ? "Protoset Loading" : "Connection Establishment");

            await using var session = await DescriptorSourceFactory.CreateAsync(
                address,
                protosets,
                channelOptions,
                reflectionMetadata,
                CancellationToken.None);

            var descriptorSource = session.Source;

            if (verbose && protosets.Length == 0)
            {
                Diagnostics.Markup("[dim]Connected successfully, querying server reflection...[/]");
            }
            else if (verbose && protosets.Length > 0)
            {
                Diagnostics.Markup("[dim]Protoset files loaded successfully[/]");
            }

            timing?.StartPhase("Schema Discovery");

            if (string.IsNullOrEmpty(service))
            {
                await ListServicesAsync(descriptorSource, verbose, output);

                // Export all services if --protoset-out specified
                if (!string.IsNullOrEmpty(protosetOut))
                {
                    var services = await descriptorSource.ListServicesAsync();

                    await ProtosetExporter.WriteProtosetAsync(descriptorSource, protosetOut, force, [.. services]);

                    if (verbose)
                    {
                        Diagnostics.Markup($"[dim]Wrote protoset to {protosetOut}[/]");
                    }
                }
            }
            else
            {
                await ListMethodsAsync(descriptorSource, service, verbose, output);

                // Export the specific service if --protoset-out specified
                if (!string.IsNullOrEmpty(protosetOut))
                {
                    await ProtosetExporter.WriteProtosetAsync(descriptorSource, protosetOut, force, [service]);

                    if (verbose)
                    {
                        Diagnostics.Markup($"[dim]Wrote protoset to {protosetOut}[/]");
                    }
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
                StatusCode.Unimplemented => new[]
                {
                    "Server does not support reflection",
                    "Use --protoset to provide schema files instead",
                    "Ask server admin to enable grpc-reflection"
                },
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
                Hint = verbose ? ex.StackTrace : ex.InnerException?.Message
            }, output);
        }
    }

    internal static async Task ListServicesAsync(IDescriptorSource descriptorSource, bool verbose, OutputFormat output = OutputFormat.Text)
    {
        if (verbose)
        {
            Diagnostics.Markup("[dim]Listing services...[/]");
        }

        var services = await descriptorSource.ListServicesAsync();

        if (services.Count == 0 && output == OutputFormat.Text)
        {
            Diagnostics.Markup("[yellow](No services)[/]");

            return;
        }

        OutputRenderer.WriteListServices(services, output);
    }

    internal static async Task ListMethodsAsync(IDescriptorSource descriptorSource, string serviceName, bool verbose, OutputFormat output = OutputFormat.Text)
    {
        if (verbose)
        {
            Diagnostics.Markup($"[dim]Finding service '{serviceName}'...[/]");
        }

        var descriptor = await descriptorSource.FindSymbolAsync(serviceName);

        if (descriptor is not ServiceDescriptor serviceDescriptor)
        {
            var envelope = new ErrorEnvelope
            {
                Category = ErrorCategory.Schema,
                ExitCode = 3,
                Message = $"'{serviceName}' is not a service"
            };

            ErrorRenderer.Render(envelope, output);

            throw new GrpcCommandException(envelope.Message, envelope.ExitCode, silent: true) { Envelope = envelope };
        }

        if (verbose)
        {
            Diagnostics.Markup($"[dim]Found service with {serviceDescriptor.Methods.Count} method(s)[/]");
        }

        if (serviceDescriptor.Methods.Count == 0 && output == OutputFormat.Text)
        {
            Diagnostics.Markup("[yellow](No methods)[/]");

            return;
        }

        OutputRenderer.WriteListMethods(serviceName, serviceDescriptor, output);
    }
}