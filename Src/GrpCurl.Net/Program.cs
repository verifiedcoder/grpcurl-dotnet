using GrpCurl.Net.Commands;
using GrpCurl.Net.Utilities;
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
catch (Exception ex)
{
    // Unexpected exception - display full details on stderr so stdout stays clean for data
    Diagnostics.Stderr.WriteException(ex);

    return 1;
}