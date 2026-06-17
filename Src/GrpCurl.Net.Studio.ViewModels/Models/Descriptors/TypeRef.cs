namespace GrpCurl.Net.Studio.ViewModels.Models.Descriptors;

/// <summary>
///     A reference to another symbol from within a describe view (FR-051). <see cref="Resolvable" />
///     is <see langword="false" /> when the type is not present in the active descriptor set, so the
///     UI renders it as plain text rather than a broken link (FR-058).
/// </summary>
public sealed record TypeRef(string FullName, bool Resolvable)
{
    /// <summary>FR-058: hover text — the FQN, or an explanation when the type isn't in the active set.</summary>
    public string Tooltip => Resolvable ? FullName : $"{FullName} — type not in the active descriptor set";
}

/// <summary>An entry in the explorer's Types branch (FR-022): a message or enum, grouped by package.</summary>
public sealed record TypeEntry(string FullName, TypeNodeKind Kind, string Package, bool Deprecated = false);

/// <summary>Whether a <see cref="TypeEntry" /> is a message or an enum.</summary>
public enum TypeNodeKind
{
    Message,
    Enum
}
