using System.Diagnostics;
using GrpCurl.Net.Studio.ViewModels.Models;
using GrpCurl.Net.Studio.ViewModels.Services;

namespace GrpCurl.Net.Studio.Services;

/// <summary>
///     Default <see cref="IProtocService" />: scans <c>PATH</c> for <c>protoc</c> (mirroring Core's
///     lookup) and runs <c>--version</c> to report what's available (FR-154). Never throws to the UI —
///     failures become a not-found <see cref="ProtocInfo" />.
/// </summary>
internal sealed class ProtocService : IProtocService
{
    public async Task<ProtocInfo> DetectAsync(CancellationToken cancellationToken = default)
    {
        var path = FindOnPath();

        if (path is null)
        {
            return ProtocInfo.NotFound("protoc was not found on PATH.");
        }

        var version = await ReadVersionAsync(path, cancellationToken).ConfigureAwait(false);
        return version is null
            ? ProtocInfo.NotFound($"Found {path} but it did not report a version.")
            : ProtocInfo.Ok(path, version);
    }

    public async Task<ProtocInfo> VerifyAsync(string path, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return ProtocInfo.NotFound("No protoc path is set.");
        }

        if (!File.Exists(path))
        {
            return ProtocInfo.NotFound($"No file exists at '{path}'.");
        }

        var version = await ReadVersionAsync(path, cancellationToken).ConfigureAwait(false);
        return version is null
            ? ProtocInfo.NotFound($"'{path}' did not respond to --version.")
            : ProtocInfo.Ok(path, version);
    }

    private static string? FindOnPath()
    {
        var executable = OperatingSystem.IsWindows() ? "protoc.exe" : "protoc";
        var pathVar = Environment.GetEnvironmentVariable("PATH");

        if (string.IsNullOrEmpty(pathVar))
        {
            return null;
        }

        foreach (var dir in pathVar.Split(Path.PathSeparator))
        {
            try
            {
                var candidate = Path.Combine(dir, executable);

                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
            catch (ArgumentException)
            {
                // Skip malformed PATH entries.
            }
        }

        return null;
    }

    private static async Task<string?> ReadVersionAsync(string path, CancellationToken cancellationToken)
    {
        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo(path, "--version")
                {
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            if (!process.Start())
            {
                return null;
            }

            var output = await process.StandardOutput.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);

            var version = output.Trim();
            return process.ExitCode == 0 && version.Length > 0 ? version : null;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
        {
            return null;
        }
    }
}
