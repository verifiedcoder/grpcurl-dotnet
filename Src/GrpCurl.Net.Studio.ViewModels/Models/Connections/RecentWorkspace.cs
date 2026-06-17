namespace GrpCurl.Net.Studio.ViewModels.Models.Connections;

/// <summary>
///     An entry in the recent-workspaces list (SPEC-040 §1): an absolute path Studio has opened or saved.
///     <see cref="Exists" /> is snapshotted when the list is built; a missing file is a dangling entry
///     (shown greyed with a "remove" affordance) since deleting/moving the file outside Studio is allowed.
/// </summary>
public sealed record RecentWorkspace(string Path, bool Exists)
{
    /// <summary>The file name without its <c>.gcnws.json</c> extension, for menu display.</summary>
    public string DisplayName
    {
        get
        {
            var name = System.IO.Path.GetFileName(Path);
            return name.EndsWith(".gcnws.json", StringComparison.OrdinalIgnoreCase)
                ? name[..^".gcnws.json".Length]
                : System.IO.Path.GetFileNameWithoutExtension(name);
        }
    }
}
