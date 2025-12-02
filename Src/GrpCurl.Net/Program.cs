using GrpCurl.Net.Commands;
using GrpCurl.Net.Exceptions;
using Spectre.Console;
using System.CommandLine;

var rootCommand = new RootCommand("grpcurl.net - A .NET implementation of grpcurl")
{
    ListCommandHandler.Create(),
    DescribeCommandHandler.Create(),
    InvokeCommandHandler.Create()
};

try
{
    var parseResult = rootCommand.Parse(args);

    return await parseResult.InvokeAsync();
}
catch (GrpcCommandException ex)
{
    // Command handler signaled an error - message already displayed to user
    // Just return the exit code without additional output
    return ex.ExitCode;
}
catch (Exception ex)
{
    // Unexpected exception - display full details
    AnsiConsole.WriteException(ex);

    return 1;
}
