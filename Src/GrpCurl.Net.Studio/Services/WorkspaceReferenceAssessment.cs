using GrpCurl.Net.Studio.ViewModels.Models.Connections;
using GrpCurl.Net.Studio.ViewModels.Services;

namespace GrpCurl.Net.Studio.Services;

/// <summary>Metadata-only assessment. External locations are never probed until their exact path is approved.</summary>
internal static class WorkspaceReferenceAssessment
{
    internal static StringComparison Comparison => OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
    internal static StringComparer Comparer => OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    internal static bool Contains(string directory, string path)
    {
        var relative = Path.GetRelativePath(directory, path);
        return !Path.IsPathRooted(relative) && relative != ".."
            && !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal)
            && !relative.StartsWith(".." + Path.AltDirectorySeparatorChar, StringComparison.Ordinal);
    }

    internal static IReadOnlyList<WorkspaceFileReference> Assess(WorkspaceModel workspace, string workspacePath,
        IReadOnlySet<string> approvals)
    {
        try { return AssessCore(workspace, workspacePath, approvals); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            throw new WorkspaceTrustException("Cannot establish workspace reference containment. Check path permissions and link targets, then try again. " + ex.Message, ex);
        }
    }

    private static IReadOnlyList<WorkspaceFileReference> AssessCore(WorkspaceModel workspace, string workspacePath,
        IReadOnlySet<string> approvals)
    {
        if (workspace.Connections is null || workspace.TlsProfiles is null
            || workspace.Connections.Any(c => c?.DescriptorSource is null || c.DescriptorSource.ProtosetPaths is null
                || c.DescriptorSource.ProtoFiles is null || c.DescriptorSource.ImportPaths is null)
            || workspace.TlsProfiles.Any(p => p is null))
        {
            throw new WorkspaceTrustException("The workspace has malformed connection or TLS file references. Correct the document before opening it.");
        }
        var directory = Path.GetDirectoryName(Path.GetFullPath(workspacePath))!;
        var aliases = new Dictionary<string, string>(Comparer);
        var physicalDirectory = ResolveTarget(directory, _ => true, aliases).Target;
        var canonicalApprovals = approvals.Select(key =>
        {
            var separator = key.IndexOf('\n');
            return key[..(separator + 1)] + ResolveKnownAliases(key[(separator + 1)..], aliases);
        }).ToHashSet(Comparer);
        bool Approved(string absolute, string target) => canonicalApprovals.Contains(absolute + "\n" + ResolveKnownAliases(target, aliases));
        var references = new List<WorkspaceFileReference>();
        var slots = WorkspacePathPortability.Enumerate(workspace).ToList();
        // protoc searches beside each source too. Make that directory authority visible to the user.
        slots.AddRange(slots.Where(s => s.Field == "Proto source").Select(s => s with
        {
            Field = "Proto source lookup directory", IsDirectory = true,
            Path = Path.GetDirectoryName(WorkspacePathPortability.Resolve(s.Path, directory))!
        }).ToArray());
        foreach (var slot in slots)
        {
            if (string.IsNullOrEmpty(slot.Path) && slot.Field.StartsWith("TLS", StringComparison.Ordinal)) { continue; }
            var absolute = WorkspacePathPortability.Resolve(slot.Path, directory);
            var candidate = Contains(directory, absolute)
                ? Path.GetFullPath(Path.GetRelativePath(directory, absolute), physicalDirectory) : absolute;
            bool Allowed(string target) => Contains(physicalDirectory, ResolveKnownAliases(target, aliases))
                || Approved(absolute, target);
            var (target, resolved) = ResolveTarget(candidate, Allowed);
            var external = !Contains(physicalDirectory, target) || !Contains(directory, absolute);
            var reason = !resolved || external && !Approved(absolute, target)
                ? target.Equals(absolute, Comparison)
                    ? "Outside the workspace directory. Approval permits access to this location."
                    : "A symbolic link or junction points outside the workspace directory. Approve this resolved target separately."
                : null;
            references.Add(new(slot.OwnerId, slot.Owner, slot.Field, slot.Path, absolute, target, slot.IsDirectory, reason));
        }
        var inspected = 0;
        foreach (var root in references.Where(r => r.IsDirectory && r.ApprovalReason is null).ToArray())
        {
            var visited = new HashSet<string>(Comparer);
            Scan(root.AbsolutePath, root.ResolvedTarget!, root, visited);
        }
        return references;

        void Scan(string logical, string physical, WorkspaceFileReference root, HashSet<string> visited)
        {
            if (!visited.Add(physical)) { return; }
            IEnumerable<string> children;
            try { children = Directory.GetFileSystemEntries(physical); }
            catch (DirectoryNotFoundException) { return; }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                throw new WorkspaceTrustException($"Cannot inspect descriptor directory '{logical}'. Check permissions or choose a narrower import directory.", ex);
            }
            foreach (var child in children)
            {
                if (++inspected > 20000)
                {
                    throw new WorkspaceTrustException("Descriptor lookup covers more than 20,000 entries. Choose narrower proto/import directories before opening this workspace.");
                }
                var logicalChild = Path.Combine(logical, Path.GetFileName(child));
                var attributes = File.GetAttributes(child);
                var isDirectory = (attributes & FileAttributes.Directory) != 0;
                var target = child;
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                {
                    bool Allowed(string candidate) => Contains(root.ResolvedTarget!, ResolveKnownAliases(candidate, aliases))
                        || Contains(physicalDirectory, ResolveKnownAliases(candidate, aliases)) || Approved(logicalChild, candidate);
                    var result = ResolveTarget(child, Allowed);
                    target = result.Target;
                    var reason = result.Resolved ? null : "A linked entry escapes its descriptor lookup directory. Approve this target separately.";
                    references.Add(new(root.OwnerId, root.Owner, "Linked descriptor lookup entry", logicalChild, logicalChild, target, isDirectory, reason));
                    if (reason is not null) { continue; }
                }
                if (isDirectory) { Scan(logicalChild, target, root, visited); }
            }
        }
    }

    // These aliases come only from resolving the user-selected workspace directory. Translating
    // them performs no additional filesystem access, including for external/network references.
    internal static string ResolveKnownAliases(string path, IReadOnlyDictionary<string, string> aliases)
    {
        for (var i = 0; i < 40; i++)
        {
            var alias = aliases.Where(a => Contains(a.Key, path)).OrderByDescending(a => a.Key.Length).FirstOrDefault();
            if (alias.Key is null) { return path; }
            path = Path.GetFullPath(Path.GetRelativePath(alias.Key, path), alias.Value);
        }
        throw new WorkspaceTrustException("Cannot resolve workspace directory aliases. Correct the link cycle and try again.");
    }

    internal static (string Target, bool Resolved) ResolveTarget(string path, Func<string, bool> mayProbe,
        IDictionary<string, string>? discoveredAliases = null)
    {
        var candidate = Path.GetFullPath(path);
        var seen = new HashSet<string>(Comparer);
        string? requiredLinkTarget = null;
        for (var links = 0; links < 40; links++)
        {
            if (!mayProbe(candidate)) { return (candidate, false); }
            if (!seen.Add(candidate)) { break; }
            var root = Path.GetPathRoot(candidate)!;
            var components = candidate[root.Length..].Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);
            var current = root;
            var restart = false;
            for (var i = 0; i < components.Length; i++)
            {
                current = Path.Combine(current, components[i]);
                FileAttributes attributes;
                try { attributes = File.GetAttributes(current); }
                catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
                {
                    if (requiredLinkTarget is not null && Contains(current, requiredLinkTarget))
                    {
                        throw new WorkspaceTrustException($"Cannot establish the target of link '{path}'. Restore its target or replace the link with a regular file/directory.", ex);
                    }
                    return (candidate, true); // Ordinary missing files are allowed; consumption reports the missing file.
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    throw new WorkspaceTrustException($"Cannot inspect '{current}'. Check its permissions or replace the link before opening this workspace.", ex);
                }
                if ((attributes & FileAttributes.ReparsePoint) == 0) { continue; }
                FileSystemInfo info = (attributes & FileAttributes.Directory) != 0 ? new DirectoryInfo(current) : new FileInfo(current);
                var link = info.LinkTarget ?? throw new WorkspaceTrustException($"Unsupported filesystem link at '{current}'. Replace it with a regular file/directory.");
                // Windows returns an extended-length target for some junctions. This spelling is
                // accepted only from filesystem metadata, never from an untrusted workspace field.
                if (OperatingSystem.IsWindows() && link.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase))
                {
                    link = @"\\" + link[8..];
                }
                else if (OperatingSystem.IsWindows() && link.StartsWith(@"\\?\", StringComparison.Ordinal))
                {
                    link = link[4..];
                }
                // GetFullPath must not collapse "directory/.." inside a link target: the named
                // directory can itself be a link, so the OS's physical parent differs from the
                // lexical parent. Leading ../ segments are safe here because current's ancestors
                // have already been resolved. Reject the ambiguous form before probing its target.
                var linkComponents = link.Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries);
                if (linkComponents.SkipWhile(c => c is "." or "..").Any(c => c == ".."))
                {
                    throw new WorkspaceTrustException($"Cannot safely resolve link '{current}': its target traverses a parent after a named directory. Recreate the link with an absolute canonical target and try again.");
                }
                candidate = WorkspacePathPortability.Resolve(link, Path.GetDirectoryName(current)!);
                if (discoveredAliases is not null) { discoveredAliases[current] = candidate; }
                requiredLinkTarget = candidate;
                for (var j = i + 1; j < components.Length; j++) { candidate = Path.Combine(candidate, components[j]); }
                restart = true;
                break;
            }
            if (!restart) { return (candidate, true); }
        }
        throw new WorkspaceTrustException($"Cannot resolve '{path}': symbolic link cycle or too many links. Correct the link and try again.");
    }
}
