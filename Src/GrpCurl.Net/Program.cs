using GrpCurl.Net.Commands;
using GrpCurl.Net.Utilities;
using Spectre.Console;
using System.CommandLine;

// Drop-in grpcurl compatibility: detect upstream-style single-dash invocations and
// rewrite into the native list/describe/invoke shape so users porting scripts don't
// have to relearn the flag spellings. The native shape stays the canonical interface.
var effectiveArgs = GrpcurlCompatHandler.TryRewrite(args) ?? args;

var rootCommand = new RootCommand("grpcurl.net - A .NET implementation of grpcurl")
{
    ListCommandHandler.Create(),
    DescribeCommandHandler.Create(),
    InvokeCommandHandler.Create()
};

try
{
    var parseResult = rootCommand.Parse(effectiveArgs);

    return await parseResult.InvokeAsync();
}
catch (Exception ex)
{
    // Unexpected exception - display full details on stderr so stdout stays clean for data
    Diagnostics.Stderr.WriteException(ex);

    return 1;
}