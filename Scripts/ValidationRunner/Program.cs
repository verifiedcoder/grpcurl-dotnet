using System.Diagnostics;
using System.Net.Sockets;
using System.Runtime.InteropServices;

// Cross-platform validation runner. Publishes the GrpCurl.Net CLI and the test server
// to a temp directory, then exercises every demo scenario against the *published*
// binaries (not `dotnet run`). Replaces the Bash production-validation flow with a
// runner that works identically on Windows, Linux, and macOS. Implements
// CODE-REVIEW.md Phase 15.

var repoRoot = LocateRepoRoot();
var ci = args.Contains("--ci");
var publishDir = Path.Combine(Path.GetTempPath(), "grpcurl-validation-" + Guid.NewGuid().ToString("N"));

Directory.CreateDirectory(publishDir);

var publishedCli = Path.Combine(publishDir, "GrpCurl.Net.dll");
var publishedServer = Path.Combine(publishDir, "GrpCurl.Net.TestServer.dll");

Console.WriteLine($"== Publishing GrpCurl.Net CLI to {publishDir}");

await RunDotnet("publish", repoRoot, ["Src/GrpCurl.Net/GrpCurl.Net.csproj", "-c", "Release", "--no-restore", "-o", publishDir]);

Console.WriteLine($"== Publishing TestServer to {publishDir}");

await RunDotnet("publish", repoRoot, ["Tests/GrpCurl.Net.TestServer/GrpCurl.Net.TestServer.csproj", "-c", "Release", "--no-restore", "-o", publishDir]);

if (!File.Exists(publishedCli))
{
    Console.Error.WriteLine($"Publish did not produce {publishedCli}.");
    Environment.Exit(1);
}

if (!File.Exists(publishedServer))
{
    Console.Error.WriteLine($"Publish did not produce {publishedServer}.");
    Environment.Exit(1);
}

var plaintextPort = FindFreePort();

Console.WriteLine($"== Starting TestServer on port {plaintextPort}");

var serverProcess = StartProcess("dotnet", [publishedServer, "--port", plaintextPort.ToString()]);

try
{
    if (!await WaitForPort("127.0.0.1", plaintextPort, TimeSpan.FromSeconds(30)))
    {
        throw new InvalidOperationException($"TestServer did not start listening on port {plaintextPort}.");
    }

    var scenarios = new List<Scenario>
    {
        new("list-services", ["list", "--plaintext", $"localhost:{plaintextPort}"], output =>
        {
            output.ShouldContain("testing.TestService");
            return true;
        }),
        new("list-methods", ["list", "--plaintext", $"localhost:{plaintextPort}", "testing.TestService"], output =>
        {
            output.ShouldContain("UnaryCall");
            return true;
        }),
        new("describe-service", ["describe", "--plaintext", $"localhost:{plaintextPort}", "testing.TestService"], output =>
        {
            output.ShouldContain("rpc UnaryCall");
            return true;
        }),
        new("invoke-empty", ["invoke", "--plaintext", "--max-time", "10s", "-d", "{}", $"localhost:{plaintextPort}", "testing.TestService/EmptyCall"], output =>
        {
            output.ShouldContain("{}");
            return true;
        }),
        new("invoke-unary", ["invoke", "--plaintext", "--max-time", "10s", "-d", "{\"responseSize\": 32}", $"localhost:{plaintextPort}", "testing.TestService/UnaryCall"], output =>
        {
            output.ShouldContain("payload");
            return true;
        }),
        new("invoke-server-streaming", ["invoke", "--plaintext", "--max-time", "10s", "-d", "{\"responseParameters\":[{\"size\":4},{\"size\":4}]}", $"localhost:{plaintextPort}", "testing.TestService/StreamingOutputCall"], output =>
        {
            // Expect at least two response objects in the output.
            return output.Split("payload", StringSplitOptions.None).Length >= 3;
        }),
        new("invoke-json-envelope", ["invoke", "--plaintext", "--output", "json", "--max-time", "10s", "-d", "{}", $"localhost:{plaintextPort}", "testing.TestService/EmptyCall"], output =>
        {
            output.ShouldContain("\"kind\":\"message\"");
            return true;
        }),
        new("invoke-bin-metadata", ["invoke", "--plaintext", "--max-time", "10s", "-H", "trace-bin: AQIDBA==", "-d", "{}", $"localhost:{plaintextPort}", "testing.TestService/EmptyCall"], output => output.Length > 0),
        new("dropin-list", ["-plaintext", $"localhost:{plaintextPort}"], output =>
        {
            output.ShouldContain("testing.TestService");
            return true;
        })
    };

    var failed = new List<string>();

    foreach (var scenario in scenarios)
    {
        Console.WriteLine($"\n== {scenario.Name}");
        Console.WriteLine($"   $ grpcurl.net {string.Join(' ', scenario.Args)}");

        var (exitCode, stdout, stderr) = await RunPublishedCli(publishedCli, scenario.Args);

        if (exitCode != 0)
        {
            failed.Add($"{scenario.Name}: exit {exitCode}\n stderr: {stderr.Trim()}");
            continue;
        }

        try
        {
            if (!scenario.Validator(stdout))
            {
                failed.Add($"{scenario.Name}: output did not match expectations\n stdout: {stdout.Trim()}");
            }
        }
        catch (Exception ex)
        {
            failed.Add($"{scenario.Name}: validator threw {ex.Message}\n stdout: {stdout.Trim()}");
        }
    }

    if (failed.Count > 0)
    {
        Console.Error.WriteLine();
        Console.Error.WriteLine("== FAILED SCENARIOS ==");

        foreach (var line in failed)
        {
            Console.Error.WriteLine(" - " + line);
        }

        Environment.Exit(1);
    }

    Console.WriteLine($"\n== {scenarios.Count} scenarios passed.");
}
finally
{
    try
    {
        if (!serverProcess.HasExited)
        {
            serverProcess.Kill(entireProcessTree: true);
        }
    }
    catch
    {
        // Best effort.
    }

    try { Directory.Delete(publishDir, recursive: true); } catch { }
}

static async Task<(int ExitCode, string StdOut, string StdErr)> RunPublishedCli(string cliDll, IReadOnlyList<string> args)
{
    var psi = new ProcessStartInfo("dotnet")
    {
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false,
        CreateNoWindow = true
    };

    psi.ArgumentList.Add(cliDll);

    foreach (var arg in args)
    {
        psi.ArgumentList.Add(arg);
    }

    using var p = Process.Start(psi) ?? throw new InvalidOperationException("Could not start CLI.");

    var stdoutTask = p.StandardOutput.ReadToEndAsync();
    var stderrTask = p.StandardError.ReadToEndAsync();

    await p.WaitForExitAsync();

    return (p.ExitCode, await stdoutTask, await stderrTask);
}

static Process StartProcess(string fileName, IReadOnlyList<string> args)
{
    var psi = new ProcessStartInfo(fileName)
    {
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false,
        CreateNoWindow = true
    };

    foreach (var arg in args)
    {
        psi.ArgumentList.Add(arg);
    }

    var p = Process.Start(psi) ?? throw new InvalidOperationException($"Could not start {fileName}.");

    _ = Task.Run(async () =>
    {
        while (await p.StandardOutput.ReadLineAsync() is { } line)
        {
            Console.WriteLine($"[server] {line}");
        }
    });

    _ = Task.Run(async () =>
    {
        while (await p.StandardError.ReadLineAsync() is { } line)
        {
            Console.Error.WriteLine($"[server.err] {line}");
        }
    });

    return p;
}

static async Task RunDotnet(string verb, string workingDirectory, IReadOnlyList<string> args)
{
    var psi = new ProcessStartInfo("dotnet")
    {
        WorkingDirectory = workingDirectory,
        UseShellExecute = false
    };

    psi.ArgumentList.Add(verb);

    foreach (var arg in args)
    {
        psi.ArgumentList.Add(arg);
    }

    using var p = Process.Start(psi) ?? throw new InvalidOperationException("Failed to start dotnet.");

    await p.WaitForExitAsync();

    if (p.ExitCode != 0)
    {
        throw new InvalidOperationException($"dotnet {verb} failed with exit code {p.ExitCode}.");
    }
}

static int FindFreePort()
{
    using var listener = new TcpListener(System.Net.IPAddress.Loopback, 0);
    listener.Start();
    var port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
    listener.Stop();
    return port;
}

static async Task<bool> WaitForPort(string host, int port, TimeSpan timeout)
{
    var deadline = DateTime.UtcNow + timeout;

    while (DateTime.UtcNow < deadline)
    {
        try
        {
            using var client = new TcpClient();
            await client.ConnectAsync(host, port);
            return true;
        }
        catch (SocketException)
        {
            await Task.Delay(200);
        }
    }

    return false;
}

static string LocateRepoRoot()
{
    var dir = AppContext.BaseDirectory;

    while (!string.IsNullOrEmpty(dir))
    {
        if (File.Exists(Path.Combine(dir, "GrpCurl.Net.slnx")))
        {
            return dir;
        }

        dir = Path.GetDirectoryName(dir);
    }

    throw new InvalidOperationException("Could not locate the repository root (GrpCurl.Net.slnx).");
}

internal sealed record Scenario(string Name, IReadOnlyList<string> Args, Func<string, bool> Validator);

internal static class StringAssert
{
    public static void ShouldContain(this string actual, string expected)
    {
        if (!actual.Contains(expected, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Expected output to contain '{expected}', got:\n{actual}");
        }
    }
}
