using System.CommandLine;
using System.Diagnostics;
using Gql2Grpc.GraphQL;
using Spectre.Console;

// Configure the CLI
var addressArg = new Argument<string>("address")
{
    Description = "gRPC server address (e.g., localhost:8080)"
};

var queryArg = new Argument<string>("query")
{
    Description = "GraphQL query string (or use -f to read from file)",
    Arity = ArgumentArity.ZeroOrOne
};

var protosetOpt = new Option<string>("--protoset")
{
    Description = "Path to protoset file",
    Arity = ArgumentArity.ExactlyOne
};

var plaintextOpt = new Option<bool>("--plaintext")
{
    Description = "Use plaintext connection (no TLS)"
};

var fileOpt = new Option<string?>("--file", "-f")
{
    Description = "Read GraphQL query from file"
};

var variablesOpt = new Option<string[]>("--var")
{
    Description = "Query variables (format: name=value)",
    Arity = ArgumentArity.ZeroOrMore
};

var verboseOpt = new Option<bool>("--verbose", "-v")
{
    Description = "Verbose output"
};

var grpcurlPathOpt = new Option<string?>("--grpcurl")
{
    Description = "Path to grpcurl-dotnet project (auto-detected if not specified)"
};

var rootCommand = new RootCommand("GraphQL-to-gRPC bridge - Execute GraphQL queries against gRPC services")
{
    addressArg,
    queryArg,
    protosetOpt,
    plaintextOpt,
    fileOpt,
    variablesOpt,
    verboseOpt,
    grpcurlPathOpt
};

rootCommand.SetAction(async (parseResult, cancellationToken) =>
{
    var address = parseResult.GetValue(addressArg)!;
    var query = parseResult.GetValue(queryArg) ?? "";
    var protoset = parseResult.GetValue(protosetOpt);
    var plaintext = parseResult.GetValue(plaintextOpt);
    var file = parseResult.GetValue(fileOpt);
    var variables = parseResult.GetValue(variablesOpt) ?? [];
    var verbose = parseResult.GetValue(verboseOpt);
    var grpcurlPath = parseResult.GetValue(grpcurlPathOpt);

    try
    {
        // Read query from file if specified
        if (!string.IsNullOrEmpty(file))
        {
            if (!File.Exists(file))
            {
                AnsiConsole.MarkupLine($"[red]Error:[/] Query file not found: {file}");
                return 1;
            }

            query = await File.ReadAllTextAsync(file, cancellationToken);
        }

        if (string.IsNullOrWhiteSpace(query))
        {
            AnsiConsole.MarkupLine("[red]Error:[/] No GraphQL query provided. Use a query argument or -f to read from file.");
            return 1;
        }

        if (string.IsNullOrEmpty(protoset))
        {
            AnsiConsole.MarkupLine("[red]Error:[/] --protoset is required.");
            return 1;
        }

        // Parse variables
        var varsDict = new Dictionary<string, string>();

        foreach (var v in variables)
        {
            var parts = v.Split('=', 2);

            if (parts.Length == 2)
            {
                varsDict[parts[0]] = parts[1];
            }
        }

        // Parse the GraphQL query
        if (verbose)
        {
            AnsiConsole.MarkupLine("[dim]Parsing GraphQL query...[/]");
        }

        var parsedQuery = GraphQLQueryParser.Parse(query, varsDict);

        if (parsedQuery.Fields.Count == 0)
        {
            AnsiConsole.MarkupLine("[red]Error:[/] No query fields found in GraphQL document");
            return 1;
        }

        // Configure the translator with known mappings
        // TO DO: Read these from proto annotations
        var translator = new RelayQueryTranslator();

        ConfigureMappings(translator);

        // Find grpcurl-dotnet path
        var grpcurlProject = grpcurlPath ?? FindGrpcurlProject();

        if (grpcurlProject is null)
        {
            AnsiConsole.MarkupLine("[red]Error:[/] Could not find grpcurl-dotnet project. Use --grpcurl to specify the path.");
            return 1;
        }

        // Process each query field
        var responses = new List<(string QueryFieldName, string GrpcResponseJson)>();
        var errors = new List<(string Path, string Message)>();

        foreach (var field in parsedQuery.Fields)
        {
            var grpcRequest = translator.Translate(field);

            if (grpcRequest is null)
            {
                errors.Add((field.Name, $"Unknown query field: {field.Name}"));
                continue;
            }

            if (verbose)
            {
                AnsiConsole.MarkupLine($"[dim]Translating '{field.Name}' -> {grpcRequest.FullMethodName}[/]");
                AnsiConsole.MarkupLine($"[dim]Request: {grpcRequest.RequestJson}[/]");
            }

            // Invoke gRPC using grpcurl-dotnet
            var grpcResponse = await InvokeGrpcAsync(
                grpcurlProject,
                address,
                grpcRequest.FullMethodName,
                grpcRequest.RequestJson,
                protoset,
                plaintext,
                verbose,
                cancellationToken);

            if (grpcResponse.Success)
            {
                responses.Add((field.Name, grpcResponse.Output));
            }
            else
            {
                errors.Add((field.Name, grpcResponse.Output));
            }
        }

        // Format and output the response

        var output = errors.Count switch
        {
            > 0 when responses.Count == 0 => GraphQLResponseFormatter.FormatError(errors[0].Message, errors[0].Path),
            > 0                           => GraphQLResponseFormatter.FormatPartialResponse(responses, errors),
            _                             => responses.Count == 1 ? GraphQLResponseFormatter.FormatResponse(responses[0].QueryFieldName, responses[0].GrpcResponseJson) : GraphQLResponseFormatter.FormatResponses(responses)
        };

        Console.WriteLine(output);

        return errors.Count > 0 ? 1 : 0;
    }
    catch (Exception ex)
    {
        AnsiConsole.MarkupLine($"[red]Error:[/] {ex.Message}");

        if (verbose)
        {
            AnsiConsole.WriteException(ex);
        }

        return 1;
    }
});

var parseResult = rootCommand.Parse(args);

return await parseResult.InvokeAsync();

// TO DO: Find a better way to configure these mappings so they don't have to be hard-coded
// Configure known GraphQL -> gRPC mappings for the Service
static void ConfigureMappings(RelayQueryTranslator translator)
{
    // Service mappings
    const string discoveryService = "company.product.v1.Service";

    // Relay list pattern
    translator.RegisterMethod(
        "graphQlFieldName",
        discoveryService,
        "GrpcMethodName",
        MethodType.List);
}

static string? FindGrpcurlProject()
{
    // Try to find grpcurl-dotnet relative to the current executable
    var currentDir = AppContext.BaseDirectory;

    // Look for common relative paths
    var possiblePaths = new[]
    {
        Path.Combine(currentDir, "..", "..", "..", "..", "GrpCurl.Net", "GrpCurl.Net.csproj"),
        Path.Combine(currentDir, "..", "GrpCurl.Net", "GrpCurl.Net.csproj"),
        Path.Combine(currentDir, "GrpCurl.Net", "GrpCurl.Net.csproj"),
    };

    foreach (var path in possiblePaths)
    {
        var fullPath = Path.GetFullPath(path);

        if (File.Exists(fullPath))
        {
            return fullPath;
        }
    }

    // Try environment variable
    var envPath = Environment.GetEnvironmentVariable("GRPCURL_DOTNET_PROJECT");

    if (!string.IsNullOrEmpty(envPath) && File.Exists(envPath))
    {
        return envPath;
    }

    return null;
}

static async Task<(bool Success, string Output)> InvokeGrpcAsync(
    string grpcurlProject,
    string address,
    string fullMethodName,
    string requestJson,
    string protoset,
    bool plaintext,
    bool verbose,
    CancellationToken cancellationToken)
{
    var args = new List<string>
    {
        "run",
        "--project", grpcurlProject,
        "--no-build",
        "-c", "Release",
        "--",
        "invoke"
    };

    if (plaintext)
    {
        args.Add("--plaintext");
    }

    args.Add("--protoset");
    args.Add(protoset);

    args.Add("-d");
    args.Add(requestJson);

    // Address and method must come after all options
    args.Add(address);
    args.Add(fullMethodName);

    if (verbose)
    {
        AnsiConsole.MarkupLine($"[dim]Args: {string.Join(" | ", args)}[/]");
    }

    if (verbose)
    {
        AnsiConsole.MarkupLine($"[dim]Running: dotnet {string.Join(" ", args)}[/]");
    }

    var psi = new ProcessStartInfo
    {
        FileName = "dotnet",
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false,
        CreateNoWindow = true
    };

    foreach (var arg in args)
    {
        psi.ArgumentList.Add(arg);
    }

    using var process = Process.Start(psi);

    if (process is null)
    {
        return (false, "Failed to start grpcurl-dotnet process");
    }

    var stdout = await process.StandardOutput.ReadToEndAsync(cancellationToken);
    var stderr = await process.StandardError.ReadToEndAsync(cancellationToken);

    await process.WaitForExitAsync(cancellationToken);

    if (process.ExitCode == 0)
    {
        return (true, stdout.Trim());
    }

    var errorMessage = !string.IsNullOrWhiteSpace(stderr) ? stderr : stdout;

    return (false, errorMessage.Trim());
}
