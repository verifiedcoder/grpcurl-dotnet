using System.Globalization;
using System.Text.Json.Nodes;
using GraphQLParser.AST;

namespace Gql2Grpc.GraphQL;

/// <summary>
/// Converts GraphQL AST values to <see cref="JsonNode"/> trees. Variable references are resolved
/// against a supplied dictionary of already-coerced variable values.
/// </summary>
internal static class GraphQLValueCoercer
{
    public static JsonNode? ToJsonNode(GraphQLValue value, IReadOnlyDictionary<string, JsonNode?> variables)
    {
        return value switch
        {
            GraphQLNullValue => null,
            GraphQLBooleanValue b => JsonValue.Create(b.BoolValue),
            GraphQLStringValue s => JsonValue.Create(s.Value.ToString()),
            GraphQLIntValue i => ParseIntegerLiteral(i.Value.ToString()),
            GraphQLFloatValue f => JsonValue.Create(double.Parse(f.Value.ToString(), CultureInfo.InvariantCulture)),
            GraphQLEnumValue e => JsonValue.Create(e.Name.StringValue),
            GraphQLVariable v => ResolveVariable(v, variables),
            GraphQLListValue l => ToJsonArray(l, variables),
            GraphQLObjectValue o => ToJsonObject(o, variables),
            _ => JsonValue.Create(value.ToString())
        };
    }

    private static JsonNode? ResolveVariable(GraphQLVariable variable, IReadOnlyDictionary<string, JsonNode?> variables)
    {
        var name = variable.Name.StringValue;

        if (!variables.TryGetValue(name, out var resolved))
        {
            throw new ArgumentException($"Undefined variable '${name}' referenced but not declared or supplied.");
        }

        return resolved?.DeepClone();
    }

    private static JsonArray ToJsonArray(GraphQLListValue list, IReadOnlyDictionary<string, JsonNode?> variables)
    {
        var array = new JsonArray();

        if (list.Values is not null)
        {
            foreach (var item in list.Values)
            {
                array.Add(ToJsonNode(item, variables));
            }
        }

        return array;
    }

    private static JsonObject ToJsonObject(GraphQLObjectValue obj, IReadOnlyDictionary<string, JsonNode?> variables)
    {
        var result = new JsonObject();

        if (obj.Fields is not null)
        {
            foreach (var field in obj.Fields)
            {
                result[field.Name.StringValue] = ToJsonNode(field.Value, variables);
            }
        }

        return result;
    }

    private static JsonValue ParseIntegerLiteral(string raw)
    {
        if (long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i64))
        {
            return i64 is >= int.MinValue and <= int.MaxValue ? JsonValue.Create((int)i64)! : JsonValue.Create(i64)!;
        }

        // Too large for long — keep as string so callers can route to the right proto field (uint64, etc.).
        return JsonValue.Create(raw)!;
    }
}
