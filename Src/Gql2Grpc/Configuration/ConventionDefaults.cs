namespace Gql2Grpc.Configuration;

/// <summary>
/// Default convention helpers: Relay-style argument aliases and the PascalCase naming rule that
/// turns <c>activeResponses</c> into <c>ActiveResponses</c> for method-name fallback.
/// </summary>
internal static class ConventionDefaults
{
    public static readonly IReadOnlyDictionary<string, string> RelayArgumentAliases =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["first"] = "page_size",
            ["last"] = "page_size",
            ["after"] = "after_cursor",
            ["before"] = "before_cursor",
            ["orderBy"] = "order_by",
            ["pageSize"] = "page_size"
        };

    /// <summary>Converts a camelCase identifier to PascalCase. Leaves empty/null strings unchanged.</summary>
    public static string ToPascalCase(string input)
    {
        if (string.IsNullOrEmpty(input))
        {
            return input;
        }

        if (char.IsUpper(input[0]))
        {
            return input;
        }

        return char.ToUpperInvariant(input[0]) + input[1..];
    }

    /// <summary>Converts a camelCase/PascalCase identifier to snake_case for protobuf JSON field names.</summary>
    public static string ToSnakeCase(string input)
    {
        if (string.IsNullOrEmpty(input))
        {
            return input;
        }

        var builder = new System.Text.StringBuilder(input.Length + 8);

        for (var i = 0; i < input.Length; i++)
        {
            var c = input[i];

            if (char.IsUpper(c))
            {
                if (i > 0 && !char.IsUpper(input[i - 1]))
                {
                    builder.Append('_');
                }

                builder.Append(char.ToLowerInvariant(c));
            }
            else
            {
                builder.Append(c);
            }
        }

        return builder.ToString();
    }

    /// <summary>Merges the built-in Relay argument aliases with a user-supplied map, user map wins.</summary>
    public static IReadOnlyDictionary<string, string> MergeArgumentAliases(
        IReadOnlyDictionary<string, string> userAliases)
    {
        if (userAliases.Count == 0)
        {
            return RelayArgumentAliases;
        }

        var merged = new Dictionary<string, string>(RelayArgumentAliases, StringComparer.Ordinal);

        foreach (var (k, v) in userAliases)
        {
            merged[k] = v;
        }

        return merged;
    }
}
