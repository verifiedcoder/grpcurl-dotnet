using GrpCurl.Net.Studio.ViewModels.Models.Connections;

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
        foreach (var connection in workspace.Connections)
        {
            var ds = connection.DescriptorSource;
            ConvertList(ds.ProtosetPaths, baseDir, convert);
            ConvertList(ds.ProtoFiles, baseDir, convert);
            ConvertList(ds.ImportPaths, baseDir, convert);
        }

        foreach (var profile in workspace.TlsProfiles)
        {
            profile.CaCertPath = convert(profile.CaCertPath ?? string.Empty, baseDir);
            profile.ClientCertPath = convert(profile.ClientCertPath ?? string.Empty, baseDir);
            profile.ClientKeyPath = convert(profile.ClientKeyPath ?? string.Empty, baseDir);
        }

        return workspace;
    }

    private static void ConvertList(List<string> paths, string baseDir, Func<string, string, string?> convert)
    {
        for (var i = 0; i < paths.Count; i++)
        {
            paths[i] = convert(paths[i], baseDir) ?? paths[i];
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
        if (Path.IsPathFullyQualified(relative) || relative.StartsWith("..", StringComparison.Ordinal))
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

        return Path.IsPathFullyQualified(path)
            ? path
            : Path.GetFullPath(Path.Combine(baseDir, path));
    }

    // A faithful deep clone (ids, overflow, every field) so relativising the write copy never touches
    // the live model. Workspaces are small; a serialise round-trip keeps this robust as the model grows.
    private static WorkspaceModel Clone(WorkspaceModel workspace)
        => WorkspaceSerializer.Deserialize(WorkspaceSerializer.Serialize(workspace));
}
