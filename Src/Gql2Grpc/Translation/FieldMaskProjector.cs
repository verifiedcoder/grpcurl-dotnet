using Gql2Grpc.Configuration;
using Gql2Grpc.GraphQL;

namespace Gql2Grpc.Translation;

/// <summary>
///     Produces a <c>google.protobuf.FieldMask</c> path list from a resolved GraphQL selection tree.
///     Each leaf selection contributes one dotted, snake_case path; intermediate nodes are dropped
///     because FieldMask semantics cover their descendants. Output is in canonical JSON form
///     (comma-separated string, e.g. <c>"id,payload.body"</c>).
/// </summary>
public static class FieldMaskProjector
{
    /// <summary>
    ///     Walks <paramref name="selections" /> and returns a comma-separated list of dotted snake_case
    ///     paths suitable for <c>google.protobuf.FieldMask</c>. Returns an empty string when no leaf
    ///     selections are present.
    /// </summary>
    public static string Build(IReadOnlyList<ResolvedSelection> selections)
    {
        var paths = new List<string>();

        Collect(selections, string.Empty, paths);

        return string.Join(",", paths);
    }

    private static void Collect(IReadOnlyList<ResolvedSelection> selections, string prefix, List<string> paths)
    {
        foreach (var selection in selections)
        {
            var segment = ConventionDefaults.ToSnakeCase(selection.Name);
            var current = prefix.Length == 0 ? segment : $"{prefix}.{segment}";

            if (selection.Children.Count == 0)
            {
                paths.Add(current);
            }
            else
            {
                Collect(selection.Children, current, paths);
            }
        }
    }
}