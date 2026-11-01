using GrpCurl.Net.TestSupport;
using System.Diagnostics;
using System.Reflection;

namespace GrpCurl.Net.Tests.Unit.Invocation;

public sealed class NestingCrashProbeTests
{
    [Theory]
    [InlineData("binary")]
    [InlineData("any")]
    public async Task Deep_input_is_rejected_without_terminating_process(string mode)
    {
        if (Environment.GetEnvironmentVariable("GRPCN_NESTING_PROBE") is { } childMode)
        {
            Exception? error = null;
            var thread = new Thread(() =>
            {
                try
                {
                    var message = NestingData.Parse(NestingData.Deep(2000, childMode == "any"));
                    _ = message.ToJson();
                }
                catch (Exception ex) { error = ex; }
            }, 512 * 1024);
            thread.Start();
            thread.Join();
            error.ShouldNotBeNull().Message.ShouldContain("limit of 100");
            // The same process remains usable following the failure.
            NestingData.Parse(NestingData.Deep(2, false)).ToJson().ShouldContain("child");
            return;
        }
        var start = new ProcessStartInfo("dotnet") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        start.ArgumentList.Add(Assembly.GetExecutingAssembly().Location);
        start.ArgumentList.Add("-class");
        start.ArgumentList.Add("*NestingCrashProbeTests");
        start.Environment["GRPCN_NESTING_PROBE"] = mode;
        using var process = Process.Start(start)!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        try { await process.WaitForExitAsync(timeout.Token); }
        finally { if (!process.HasExited) { process.Kill(entireProcessTree: true); } }
        process.ExitCode.ShouldBe(0, (await stdout) + (await stderr));
    }
}
