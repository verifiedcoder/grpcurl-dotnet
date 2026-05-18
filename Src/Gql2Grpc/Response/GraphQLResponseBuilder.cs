using System.Text.Json;
using System.Text.Json.Nodes;
using Gql2Grpc.GraphQL;

namespace Gql2Grpc.Response;

/// <summary>
/// Assembles the GraphQL response envelope: <c>data</c>, <c>errors[]</c>, optional <c>extensions</c>.
/// Paths in errors are emitted as JSON arrays per the GraphQL spec. Document order is preserved
/// across root fields — the caller supplies them already sorted.
/// </summary>
public static class GraphQLResponseBuilder
{
    /// <summary>Builds the single-envelope response (unary/mutation/query).</summary>
    public static JsonObject Build(
        IReadOnlyList<RootFieldResult> fieldResults,
        IReadOnlyList<GraphQLError> additionalErrors)
    {
        var envelope = new JsonObject();
        var dataObj = new JsonObject();
        var errors = new List<GraphQLError>();

        foreach (var result in fieldResults)
        {
            if (result.Data is not null || !result.Failed)
            {
                dataObj[result.ResponseKey] = result.Data?.DeepClone();
            }
            else
            {
                dataObj[result.ResponseKey] = null;
            }

            errors.AddRange(result.Errors);
        }

        errors.AddRange(additionalErrors);

        envelope["data"] = fieldResults.Count == 0 ? null : dataObj;

        if (errors.Count > 0)
        {
            envelope["errors"] = BuildErrorsArray(errors);
        }

        return envelope;
    }

    /// <summary>
    /// Convenience constructor for an envelope containing a single top-level error and no data.
    /// Used by the command-level catch chain to surface failures that occur before the executor runs.
    /// </summary>
    public static JsonObject BuildSingleError(GraphQLError error)
    {
        var envelope = new JsonObject
        {
            ["data"] = null,
            ["errors"] = new JsonArray(ErrorToJson(error))
        };

        return envelope;
    }

    /// <summary>Serializes an envelope as pretty-printed JSON.</summary>
    public static string Serialize(JsonObject envelope) => envelope.ToJsonString(PrettyOptions);

    private static JsonArray BuildErrorsArray(IReadOnlyList<GraphQLError> errors)
    {
        var array = new JsonArray();

        foreach (var error in errors)
        {
            array.Add(ErrorToJson(error));
        }

        return array;
    }

    private static JsonObject ErrorToJson(GraphQLError error)
    {
        var json = new JsonObject
        {
            ["message"] = error.Message
        };

        if (error.Path.Count > 0)
        {
            var pathArr = new JsonArray();

            foreach (var segment in error.Path)
            {
                pathArr.Add(segment switch
                {
                    string s => JsonValue.Create(s),
                    int i => JsonValue.Create(i),
                    long l => JsonValue.Create(l),
                    _ => JsonValue.Create(segment.ToString())
                });
            }

            json["path"] = pathArr;
        }

        if (error.Extensions is { Count: > 0 } ext)
        {
            var extObj = new JsonObject();

            foreach (var (k, v) in ext)
            {
                extObj[k] = v switch
                {
                    null => null,
                    string s => JsonValue.Create(s),
                    int i => JsonValue.Create(i),
                    long l => JsonValue.Create(l),
                    bool b => JsonValue.Create(b),
                    JsonNode n => n.DeepClone(),
                    _ => JsonValue.Create(v.ToString())
                };
            }

            json["extensions"] = extObj;
        }

        return json;
    }

    private static readonly JsonSerializerOptions PrettyOptions = new()
    {
        WriteIndented = true
    };
}

/// <summary>Per-root-field execution outcome, used to build the envelope.</summary>
/// <param name="ResponseKey">Output key (alias if supplied, otherwise the field name).</param>
/// <param name="Data">The projected data value, or <c>null</c> when the field failed.</param>
/// <param name="Errors">Field-level errors emitted by execution (may be empty).</param>
/// <param name="Failed">When <c>true</c>, the field's data was not produced; the envelope renders <c>data[key]: null</c>.</param>
public sealed record RootFieldResult(
    string ResponseKey,
    JsonNode? Data,
    IReadOnlyList<GraphQLError> Errors,
    bool Failed);
