using Gql2Grpc.Configuration;
using Gql2Grpc.GraphQL;
using System.Text.Json.Nodes;

namespace Gql2Grpc.Translation;

/// <summary>
///     Applies mapping-entry argument rules (and convention fallback) to a resolved GraphQL selection
///     to produce the request JSON accepted by GrpCurl.Net's dynamic invocation.
/// </summary>
public sealed class JsonRequestTranslator : IRequestTranslator
{
    /// <inheritdoc />
    public string Translate(ResolvedSelection root, MappingEntry entry, MappingDefaults defaults)
    {
        var request = new JsonObject();

        // 1. Literals are always applied, even when the caller didn't supply the argument.
        foreach (var (argName, rule) in entry.Arguments)
        {
            if (rule is not ArgumentRule.Literal literal)
            {
                continue;
            }

            var targetPath = ResolveTargetPath(argName, entry, defaults);

            SetAtPath(request, targetPath, JsonValue.Create(literal.Value));
        }

        // 2. Caller-supplied arguments route through their matching rule, or the convention fallback.
        foreach (var (argName, argValue) in root.Arguments)
        {
            ApplyCallerArgument(request, argName, argValue, entry, defaults);
        }

        // 3. $selection.fieldMask — derive a FieldMask from the resolved selection tree.
        if (entry.SelectionFieldMaskPath is not { } maskPath)
        {
            return request.ToJsonString();
        }

        var mask = FieldMaskProjector.Build(root.Children);

        if (!string.IsNullOrEmpty(mask))
        {
            SetAtPath(request, maskPath, JsonValue.Create(mask));
        }

        return request.ToJsonString();
    }

    private static void ApplyCallerArgument(
        JsonObject request,
        string argName,
        JsonNode? value,
        MappingEntry entry,
        MappingDefaults defaults)
    {
        if (entry.Arguments.TryGetValue(argName, out var rule))
        {
            switch (rule)
            {
                case ArgumentRule.SkipArgument:

                    return;

                case ArgumentRule.Literal:

                    // Already applied during phase 1; callers don't override literals.
                    return;

                case ArgumentRule.PathRule { Path: "." }:

                    SpreadOntoRoot(request, value);

                    return;

                case ArgumentRule.PathRule path:

                    SetAtPath(request, path.Path, value?.DeepClone());

                    return;

                case ArgumentRule.Rename rename:

                    SetAtPath(request, rename.GrpcFieldName, value?.DeepClone());

                    return;
            }
        }

        var conventionTarget = ResolveTargetPath(argName, entry, defaults);

        SetAtPath(request, conventionTarget, value?.DeepClone());
    }

    private static string ResolveTargetPath(string argName, MappingEntry entry, MappingDefaults defaults)
    {
        if (entry.Arguments.TryGetValue(argName, out var rule))
        {
            return rule switch
            {
                ArgumentRule.PathRule p when p.Path != "." => p.Path,
                ArgumentRule.Rename r                      => r.GrpcFieldName,
                _                                          => ConventionDefaults.ToSnakeCase(argName)
            };
        }

        var aliases = ConventionDefaults.MergeArgumentAliases(defaults.ArgumentAliases);

        return aliases.TryGetValue(argName, out var alias) ? alias : ConventionDefaults.ToSnakeCase(argName);
    }

    private static void SpreadOntoRoot(JsonObject request, JsonNode? value)
    {
        if (value is not JsonObject obj)
        {
            // Spreading a non-object (null, scalar, array) is a caller error, but we fail loudly upstream
            // rather than silently dropping data.
            throw new ArgumentException(
                "Argument rule { path: \".\" } requires an object value; got " +
                (value?.GetType().Name ?? "null"));
        }

        foreach (var (key, child) in obj)
        {
            request[key] = child?.DeepClone();
        }
    }

    private static void SetAtPath(JsonObject root, string path, JsonNode? value)
    {
        if (string.IsNullOrEmpty(path))
        {
            return;
        }

        var segments = path.Split('.');
        var current = root;

        for (var i = 0; i < segments.Length - 1; i++)
        {
            var segment = segments[i];

            if (current[segment] is JsonObject next)
            {
                current = next;
            }
            else
            {
                var created = new JsonObject();
                current[segment] = created;
                current = created;
            }
        }

        current[segments[^1]] = value;
    }
}