using System.Text.Json.Nodes;
using Gql2Grpc.Configuration;
using Gql2Grpc.GraphQL;

namespace Gql2Grpc.Response;

/// <summary>
/// Prunes a gRPC response JSON tree to match a GraphQL selection. Source field names are
/// assumed to be snake_case (canonical protobuf JSON); both snake_case and the verbatim
/// GraphQL name are tried for compatibility with servers that emit camelCase. Aliases are
/// preserved as output keys. An <see cref="ResponseShaping.Unwrap"/> hint strips a single
/// wrapper field before projection (for APIs that wrap lists in <c>items</c> etc.).
/// </summary>
/// <remarks>
/// Creates a projector. When <paramref name="strict"/> is <c>true</c>, fields requested in the
/// selection but absent from the gRPC response are reported as <c>MISSING_FIELD</c> errors;
/// otherwise they are emitted as <c>null</c>.
/// </remarks>
public sealed class SelectionProjector(bool strict)
{
    private readonly bool _strict = strict;

    /// <summary>Projects the full response for a single root field.</summary>
    public JsonNode? Project(
        JsonNode? source,
        IReadOnlyList<ResolvedSelection> selections,
        ResponseShaping? shaping,
        IReadOnlyList<object> basePath,
        List<GraphQLError> errorSink)
    {
        if (shaping?.Unwrap is { } unwrap && source is JsonObject wrapObject &&
            wrapObject.TryGetPropertyValue(unwrap, out var unwrapped))
        {
            source = unwrapped;
        }

        return ProjectInternal(source, selections, basePath, errorSink);
    }

    private JsonNode? ProjectInternal(
        JsonNode? source,
        IReadOnlyList<ResolvedSelection> selections,
        IReadOnlyList<object> path,
        List<GraphQLError> errorSink)
    {
        if (source is null)
        {
            return null;
        }

        if (selections.Count == 0)
        {
            return source.DeepClone();
        }

        return source switch
        {
            JsonArray array => ProjectArray(array, selections, path, errorSink),
            JsonObject obj => ProjectObject(obj, selections, path, errorSink),
            _ => source.DeepClone() // Value/Struct scalar passthrough
        };
    }

    private JsonArray ProjectArray(
        JsonArray array,
        IReadOnlyList<ResolvedSelection> selections,
        IReadOnlyList<object> path,
        List<GraphQLError> errorSink)
    {
        var result = new JsonArray();

        for (var i = 0; i < array.Count; i++)
        {
            var elementPath = AppendPath(path, i);
            result.Add(ProjectInternal(array[i], selections, elementPath, errorSink));
        }

        return result;
    }

    private JsonObject ProjectObject(
        JsonObject source,
        IReadOnlyList<ResolvedSelection> selections,
        IReadOnlyList<object> path,
        List<GraphQLError> errorSink)
    {
        var result = new JsonObject();

        foreach (var selection in selections)
        {
            var childPath = AppendPath(path, selection.ResponseKey);
            var child = LookupField(source, selection.Name);

            if (child is null && !source.ContainsKey(selection.Name) && !source.ContainsKey(ConventionDefaults.ToSnakeCase(selection.Name)))
            {
                if (_strict)
                {
                    errorSink.Add(new GraphQLError(
                        $"Field '{selection.ResponseKey}' was not present in the gRPC response.",
                        childPath,
                        new Dictionary<string, object?> { ["code"] = "MISSING_FIELD" }));
                }

                result[selection.ResponseKey] = null;
                continue;
            }

            result[selection.ResponseKey] = ProjectInternal(child, selection.Children, childPath, errorSink);
        }

        return result;
    }

    private static JsonNode? LookupField(JsonObject source, string gqlName)
    {
        if (source.TryGetPropertyValue(gqlName, out var direct))
        {
            return direct;
        }

        var snake = ConventionDefaults.ToSnakeCase(gqlName);

        return source.TryGetPropertyValue(snake, out var viaSnake) ? viaSnake : null;
    }

    private static List<object> AppendPath(IReadOnlyList<object> path, object segment)
    {
        var next = new List<object>(path.Count + 1);
        next.AddRange(path);
        next.Add(segment);
        return next;
    }
}
