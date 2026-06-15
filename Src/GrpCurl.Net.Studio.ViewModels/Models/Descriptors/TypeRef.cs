namespace GrpCurl.Net.Studio.ViewModels.Models.Descriptors;

/// <summary>
///     A reference to another symbol from within a describe view (FR-051). <see cref="Resolvable" />
///     is <see langword="false" /> when the type is not present in the active descriptor set, so the
///     UI renders it as plain text rather than a broken link (FR-058).
/// </summary>
public sealed record TypeRef(string FullName, bool Resolvable);

/// <summary>An entry in the explorer's Types branch (FR-022): a message or enum, grouped by package.</summary>
public sealed record TypeEntry(string FullName, TypeNodeKind Kind, string Package);

/// <summary>Whether a <see cref="TypeEntry" /> is a message or an enum.</summary>
public enum TypeNodeKind
{
    Message,
    Enum
}
