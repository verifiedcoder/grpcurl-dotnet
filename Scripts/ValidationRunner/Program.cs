using System.Diagnostics;
using System.Net.Sockets;

namespace GrpCurl.Net.ValidationRunner;

// Cross-platform validation runner. Publishes the GrpCurl.Net CLI and the test server
// to a temp directory, then exercises every demo scenario against the published binaries.
internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        string? publishDir = null;
        Process? serverProcess = null;

        try
        {
            ValidateArguments(args);

            var repoRoot = LocateRepoRoot();
            publishDir = Path.Combine(Path.GetTempPath(), "grpcurl-validation-" + Guid.NewGuid().ToString("N"));

            Directory.CreateDirectory(publishDir);

            var publishedCli = Path.Combine(publishDir, "GrpCurl.Net.dll");
            var publishedServer = Path.Combine(publishDir, "GrpCurl.Net.TestServer.dll");
            var artifactsDir = Path.Combine(publishDir, "artifacts");

            await Console.Out.WriteLineAsync($"== Publishing GrpCurl.Net CLI to {publishDir}");

            await PublishProject(repoRoot, "Src/GrpCurl.Net/GrpCurl.Net.csproj", publishDir, artifactsDir).ConfigureAwait(false);

            await Console.Out.WriteLineAsync($"== Publishing TestServer to {publishDir}");

            await PublishProject(repoRoot, "Tests/GrpCurl.Net.TestServer/GrpCurl.Net.TestServer.csproj", publishDir, artifactsDir).ConfigureAwait(false);

            if (!File.Exists(publishedCli))
            {
                throw new InvalidOperationException($"Publish did not produce {publishedCli}.");
            }

            if (!File.Exists(publishedServer))
            {
                throw new InvalidOperationException($"Publish did not produce {publishedServer}.");
            }

            var plaintextPort = FindFreePort();

            await Console.Out.WriteLineAsync($"== Starting TestServer on port {plaintextPort}");

            serverProcess = StartProcess("dotnet", [publishedServer, "--port", plaintextPort.ToString()]);

            if (!await WaitForPort("127.0.0.1", plaintextPort, TimeSpan.FromSeconds(30)).ConfigureAwait(false))
            {
                throw new InvalidOperationException($"TestServer did not start listening on port {plaintextPort}.");
            }

            var scenarios = BuildScenarios(plaintextPort);
            var failed = new List<string>();

            foreach (var scenario in scenarios)
            {
                await Console.Out.WriteLineAsync($"\n== {scenario.Name}");
                await Console.Out.WriteLineAsync($"   $ grpcurl.net {string.Join(' ', scenario.Args)}");

                var (exitCode, stdout, stderr) = await RunPublishedCli(publishedCli, scenario.Args).ConfigureAwait(false);

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
                await Console.Error.WriteLineAsync();
                await Console.Error.WriteLineAsync("== FAILED SCENARIOS ==");

                foreach (var line in failed)
                {
                    await Console.Error.WriteLineAsync(" - " + line);
                }

                throw new InvalidOperationException($"{failed.Count} validation scenario(s) failed.");
            }

            await Console.Out.WriteLineAsync($"\n== {scenarios.Count} scenarios passed.");
            return 0;
        }
        catch (Exception ex)
        {
            await Console.Error.WriteLineAsync();
            await Console.Error.WriteLineAsync("== VALIDATION RUNNER FAILED ==");
            await Console.Error.WriteLineAsync(ex.Message);
            return 1;
        }
        finally
        {
            StopProcess(serverProcess);
            DeleteDirectory(publishDir);
        }
    }

    private static List<Scenario> BuildScenarios(int plaintextPort) =>
    [
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
        new("invoke-server-streaming", ["invoke", "--plaintext", "--max-time", "10s", "-d", "{\"responseParameters\":[{\"size\":4},{\"size\":4}]}", $"localhost:{plaintextPort}", "testing.TestService/StreamingOutputCall"], output => output.Split("payload").Length >= 3),
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
    ];

    private static void ValidateArguments(IEnumerable<string> args)
    {
        if (args.FirstOrDefault(arg => !string.Equals(arg, "--ci", StringComparison.Ordinal)) is { } unknownArg)
        {
            throw new ArgumentException($"Unknown argument '{unknownArg}'.");
        }
    }

    private static async Task<(int ExitCode, string StdOut, string StdErr)> RunPublishedCli(string cliDll, IReadOnlyList<string> args)
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

        using var process = Process.Start(psi) ?? throw new InvalidOperationException("Could not start CLI.");

        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();

        await process.WaitForExitAsync().ConfigureAwait(false);

        return (process.ExitCode, await stdoutTask.ConfigureAwait(false), await stderrTask.ConfigureAwait(false));
    }

    private static Process StartProcess(string fileName, IReadOnlyList<string> args)
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

        var process = Process.Start(psi) ?? throw new InvalidOperationException($"Could not start {fileName}.");

        _ = Task.Run(() => DrainOutputAsync(process.StandardOutput, Console.Out, "[server] "));
        _ = Task.Run(() => DrainOutputAsync(process.StandardError, Console.Error, "[server.err] "));

        return process;
    }

    private static async Task DrainOutputAsync(TextReader reader, TextWriter writer, string prefix)
    {
        while (await reader.ReadLineAsync().ConfigureAwait(false) is { } line)
        {
            await writer.WriteLineAsync(prefix + line).ConfigureAwait(false);
        }
    }

    private static async Task RunDotnet(string verb, string workingDirectory, IReadOnlyList<string> args)
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

        using var process = Process.Start(psi) ?? throw new InvalidOperationException("Failed to start dotnet.");

        await process.WaitForExitAsync().ConfigureAwait(false);

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"dotnet {verb} {string.Join(' ', args)} failed with exit code {process.ExitCode}.");
        }
    }

    private static async Task PublishProject(string repoRoot, string projectPath, string publishDir, string artifactsDir)
    {
        await RunDotnet(
            "restore",
            repoRoot,
            [projectPath, "--locked-mode", "--artifacts-path", artifactsDir, "-p:NuGetAudit=false"]).ConfigureAwait(false);

        await RunDotnet(
            "publish",
            repoRoot,
            [projectPath, "-c", "Release", "--no-restore", "--artifacts-path", artifactsDir, "-o", publishDir]).ConfigureAwait(false);
    }

    private static int FindFreePort()
    {
        using var listener = new TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        var port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private static async Task<bool> WaitForPort(string host, int port, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;

        while (DateTime.UtcNow < deadline)
        {
            try
            {
                using var client = new TcpClient();

                await client.ConnectAsync(host, port).ConfigureAwait(false);

                return true;
            }
            catch (SocketException)
            {
                await Task.Delay(200).ConfigureAwait(false);
            }
        }

        return false;
    }

    private static string LocateRepoRoot()
    {
        var candidates = new[]
        {
            Directory.GetCurrentDirectory(),
            AppContext.BaseDirectory
        };

        foreach (var candidate in candidates)
        {
            var dir = candidate;

            while (!string.IsNullOrEmpty(dir))
            {
                if (File.Exists(Path.Combine(dir, "GrpCurl.Net.slnx")))
                {
                    return dir;
                }

                dir = Path.GetDirectoryName(dir);
            }
        }

        throw new InvalidOperationException("Could not locate the repository root (GrpCurl.Net.slnx).");
    }

    private static void StopProcess(Process? process)
    {
        if (process is null)
        {
            return;
        }

        try
        {
            if (process.HasExited)
            {
                return;
            }

            process.Kill(entireProcessTree: true);
            process.WaitForExit(5000);
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            Console.Error.WriteLine($"Failed to stop validation server: {ex.Message}");
        }
        finally
        {
            process.Dispose();
        }
    }

    private static void DeleteDirectory(string? path)
    {
        if (string.IsNullOrEmpty(path) || !Directory.Exists(path))
        {
            return;
        }

        try
        {
            Directory.Delete(path, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"Failed to delete temporary directory '{path}': {ex.Message}");
        }
    }
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
