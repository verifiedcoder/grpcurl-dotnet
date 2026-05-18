using System.Diagnostics;
using Google.Protobuf;
using Google.Protobuf.Reflection;

namespace GrpCurl.Net.DescriptorSources;

/// <summary>
///     Builds a descriptor source from one or more <c>.proto</c> source files plus optional
///     import roots. Shells out to <c>protoc</c> to produce a <see cref="FileDescriptorSet"/>
///     and then loads it through the same code path as <see cref="ProtosetSource"/>. This
///     gives users with a normal proto tree the same workflow as upstream grpcurl's
///     <c>-proto</c> + <c>-import-path</c> flags.
/// </summary>
internal static class ProtoSource
{
    /// <summary>
    ///     Generates a protoset from the given <c>.proto</c> files and import roots, then
    ///     returns a <see cref="ProtosetSource"/> wrapping it.
    /// </summary>
    /// <param name="protoFiles">Paths to <c>.proto</c> files. Relative paths are resolved
    /// against the cwd first, then against each entry in <paramref name="importPaths"/>.</param>
    /// <param name="importPaths">Directories passed to <c>protoc</c> as <c>-I</c>. The
    /// directory of each proto file is also included automatically.</param>
    /// <param name="cancellationToken">Cancels both the <c>protoc</c> child process and
    /// the descriptor load.</param>
    public static async Task<ProtosetSource> LoadFromProtoFilesAsync(
        IReadOnlyList<string> protoFiles,
        IReadOnlyList<string> importPaths,
        CancellationToken cancellationToken)
    {
        if (protoFiles.Count == 0)
        {
            throw new ArgumentException("At least one .proto file is required.", nameof(protoFiles));
        }

        var protocPath = FindProtoc()
            ?? throw new FileNotFoundException(
                "protoc not found on PATH. Install Protocol Buffers compiler " +
                "(e.g. 'apt install protobuf-compiler', 'brew install protobuf', " +
                "or 'choco install protoc') and retry. " +
                "Alternative: pre-compile with 'protoc --descriptor_set_out=svc.protoset " +
                "--include_imports *.proto' and pass --protoset instead.");

        // Collect import paths: explicit -I roots + each proto file's directory + cwd.
        var allImportPaths = importPaths
            .Where(Directory.Exists)
            .Select(Path.GetFullPath)
            .ToList();

        foreach (var proto in protoFiles)
        {
            var dir = Path.GetDirectoryName(Path.GetFullPath(proto));

            if (!string.IsNullOrEmpty(dir) && !allImportPaths.Contains(dir))
            {
                allImportPaths.Add(dir);
            }
        }

        allImportPaths.Add(Environment.CurrentDirectory);

        var tempProtoset = CreateTempProtosetPath();

        try
        {
            var args = new List<string>
            {
                "--include_imports",
                "--include_source_info",
                $"--descriptor_set_out={tempProtoset}"
            };

            foreach (var path in allImportPaths)
            {
                args.Add($"-I{path}");
            }

            foreach (var proto in protoFiles)
            {
                args.Add(proto);
            }

            var psi = new ProcessStartInfo(protocPath)
            {
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            foreach (var arg in args)
            {
                psi.ArgumentList.Add(arg);
            }

            using var process = Process.Start(psi)
                ?? throw new InvalidOperationException($"Failed to start protoc at '{protocPath}'.");

            using (cancellationToken.Register(() =>
            {
                try { process.Kill(entireProcessTree: true); } catch { /* race with normal exit */ }
            }))
            {
                var stderr = await process.StandardError.ReadToEndAsync(cancellationToken).ConfigureAwait(false);

                await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);

                if (process.ExitCode != 0)
                {
                    throw new InvalidOperationException(
                        $"protoc failed with exit code {process.ExitCode}:{Environment.NewLine}{stderr}");
                }
            }

            return await ProtosetSource.LoadFromFilesAsync([tempProtoset], cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            try { File.Delete(tempProtoset); } catch { /* best effort */ }
        }
    }

    private static string? FindProtoc()
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
                // Skip malformed PATH entries (e.g. embedded null chars on Windows).
            }
        }

        return null;
    }

    private static string CreateTempProtosetPath()
    {
        var path = Path.Combine(Path.GetTempPath(), $"grpcurl-{Guid.NewGuid():N}.protoset");
        using var _ = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        return path;
    }
}
