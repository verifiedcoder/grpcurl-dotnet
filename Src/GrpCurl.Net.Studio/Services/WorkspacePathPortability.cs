using GrpCurl.Net.Studio.ViewModels.Models.Connections;
using GrpCurl.Net.Studio.ViewModels.Services;

namespace GrpCurl.Net.Studio.Services;

/// <summary>
///     FR-147: makes file references (certs, protosets, protos, import dirs) portable. On <em>write</em>,
///     a path that lives beneath the workspace file's directory is stored relative to it (forward-slash,
///     OS-neutral); a path elsewhere is kept absolute. On <em>read</em>, relative paths are resolved back
///     to absolute against the same directory. The in-memory model therefore always holds absolute paths
///     — only the on-disk form is relativised — so the rest of the app (connecting, file pickers,
///     existence checks) is unaffected. A repo-committed workspace whose certs/protos live alongside it
///     stays valid across checkouts and machines.
/// </summary>
internal static class WorkspacePathPortability
{
    /// <summary>
    ///     Returns a deep copy of <paramref name="workspace" /> whose file references are relative to
    ///     <paramref name="workspaceDirectory" /> when they live beneath it. The live model is never mutated.
    /// </summary>
    public static WorkspaceModel ToRelative(WorkspaceModel workspace, string workspaceDirectory)
        => Transform(Clone(workspace), workspaceDirectory, MakeRelative);

    /// <summary>
    ///     Resolves the (freshly deserialised, caller-owned) <paramref name="workspace" />'s relative file
    ///     references to absolute paths against <paramref name="workspaceDirectory" />, in place.
    /// </summary>
    public static WorkspaceModel ToAbsolute(WorkspaceModel workspace, string workspaceDirectory)
        => Transform(workspace, workspaceDirectory, MakeAbsolute);

    private static WorkspaceModel Transform(WorkspaceModel workspace, string baseDir, Func<string, string, string?> convert)
    {
        foreach (var reference in Enumerate(workspace))
        {
            reference.Set(convert(reference.Path, baseDir));
        }
        return workspace;
    }

    internal sealed record Reference(string OwnerId, string Owner, string Field, string Path, bool IsDirectory, Action<string?> Set);

    internal static IEnumerable<Reference> Enumerate(WorkspaceModel workspace)
    {
        foreach (var connection in workspace.Connections)
        {
            foreach (var (field, paths, directory) in new[]
            {
                ("Descriptor protoset", connection.DescriptorSource.ProtosetPaths, false),
                ("Proto source", connection.DescriptorSource.ProtoFiles, false),
                ("Proto import directory", connection.DescriptorSource.ImportPaths, true)
            })
            {
                for (var i = 0; i < paths.Count; i++)
                {
                    var index = i;
                    yield return new(connection.Id, connection.Name, field, paths[i], directory, value => paths[index] = value ?? string.Empty);
                }
            }
        }
        foreach (var profile in workspace.TlsProfiles)
        {
            yield return new(profile.Id, profile.Name, "TLS CA certificate", profile.CaCertPath ?? string.Empty, false, value => profile.CaCertPath = value);
            yield return new(profile.Id, profile.Name, "TLS client certificate", profile.ClientCertPath ?? string.Empty, false, value => profile.ClientCertPath = value);
            yield return new(profile.Id, profile.Name, "TLS client private key", profile.ClientKeyPath ?? string.Empty, false, value => profile.ClientKeyPath = value);
        }
    }

    /// <summary>An absolute path beneath <paramref name="baseDir" /> becomes a forward-slash relative path; otherwise unchanged.</summary>
    internal static string? MakeRelative(string path, string baseDir)
    {
        if (string.IsNullOrEmpty(path) || !Path.IsPathFullyQualified(path))
        {
            return string.IsNullOrEmpty(path) ? null : path; // already relative — leave as-is
        }

        var relative = Path.GetRelativePath(baseDir, path);

        // GetRelativePath returns a rooted path (or the input verbatim) when no relative route exists
        // (different drive/root); a leading ".." means the target sits outside the workspace directory.
        if (!WorkspaceReferenceAssessment.Contains(baseDir, path))
        {
            return path;
        }

        return relative.Replace('\\', '/');
    }

    /// <summary>A relative path is resolved against <paramref name="baseDir" />; an absolute path is left unchanged.</summary>
    internal static string? MakeAbsolute(string path, string baseDir)
    {
        if (string.IsNullOrEmpty(path))
        {
            return null;
        }

        return Resolve(path, baseDir);
    }

    internal static string Resolve(string path, string baseDir)
    {
        // Reject foreign platform syntax rather than interpreting it as a local filename. Drive-relative,
        // root-relative Windows paths, device namespaces, ADS, and trailing-dot aliases are ambiguous.
        if (string.IsNullOrWhiteSpace(path) || path.Any(char.IsControl)
            || path != path.Trim() || path.IndexOfAny(['*', '?', '"', '<', '>', '|']) >= 0
            || (!OperatingSystem.IsWindows() && (path.Contains('\\') || path.Contains(':') || path.StartsWith("//", StringComparison.Ordinal)))
            || (OperatingSystem.IsWindows() && (Path.IsPathRooted(path) && !Path.IsPathFullyQualified(path)
                || path.StartsWith(@"\\.\", StringComparison.Ordinal) || path.StartsWith(@"\\?\", StringComparison.Ordinal)
                || path[System.IO.Path.GetPathRoot(path)!.Length..].Contains(':')
                || path.Split(['/', '\\']).Any(p => p is not "." and not ".." && (p.EndsWith('.') || p.EndsWith(' ') || IsWindowsDevice(p))))))
        {
            throw new WorkspaceTrustException($"Ambiguous file reference '{path}'. Use a normal absolute path for this platform or a relative path with forward slashes.");
        }
        try { return Path.GetFullPath(path, baseDir); }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            throw new WorkspaceTrustException($"Invalid file reference '{path}'. Correct it in the workspace and try again.", ex);
        }
    }

    private static bool IsWindowsDevice(string component)
    {
        var stem = component.Split('.')[0].ToUpperInvariant();
        return stem is "CON" or "PRN" or "AUX" or "NUL" or "CONIN$" or "CONOUT$"
            || stem.Length > 3 && (stem.StartsWith("COM", StringComparison.Ordinal) || stem.StartsWith("LPT", StringComparison.Ordinal))
                && stem[3..].All(char.IsNumber);
    }

    // A faithful deep clone (ids, overflow, every field) so relativising the write copy never touches
    // the live model. Workspaces are small; a serialise round-trip keeps this robust as the model grows.
    private static WorkspaceModel Clone(WorkspaceModel workspace)
        => WorkspaceSerializer.Deserialize(WorkspaceSerializer.Serialize(workspace));
}
