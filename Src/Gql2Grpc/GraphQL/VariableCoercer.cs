using GraphQLParser.AST;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Gql2Grpc.GraphQL;

/// <summary>
///     Resolves GraphQL operation variables against CLI <c>--var</c> pairs, a JSON variables file,
///     and variable definition defaults. Produces a dictionary keyed by variable name with
///     <see cref="JsonNode" /> values (or <c>null</c> for explicit null literals). Throws for missing
///     required (non-null, no-default) variables.
/// </summary>
public static class VariableCoercer
{
    /// <summary>
    ///     Coerces operation variables from CLI <c>--var</c> pairs and a JSON variables file
    ///     against their declared GraphQL types.
    /// </summary>
    /// <param name="definitions">Variable definitions from the selected operation.</param>
    /// <param name="cliVariables">CLI-supplied <c>name=value</c> pairs (optional).</param>
    /// <param name="variablesFile">Already-parsed contents of <c>--variables-file</c> (optional).</param>
    /// <returns>Resolved variables keyed by name, with <c>JsonNode</c> values (or <c>null</c> for explicit nulls).</returns>
    /// <exception cref="ArgumentException">
    ///     Thrown when a non-null variable is missing, a value's type is invalid, or a list-typed
    ///     variable is supplied via <c>--var</c> (use <c>--variables-file</c> for lists).
    /// </exception>
    public static IReadOnlyDictionary<string, JsonNode?> Coerce(
        IReadOnlyList<GraphQLVariableDefinition> definitions,
        IReadOnlyDictionary<string, string>? cliVariables,
        JsonNode? variablesFile)
    {
        var result = new Dictionary<string, JsonNode?>(StringComparer.Ordinal);

        foreach (var def in definitions)
        {
            JsonNode? value = null;

            var name = def.Variable.Name.StringValue;
            var declaredType = def.Type;
            var source = VariableSource.Missing;

            if (cliVariables is not null && cliVariables.TryGetValue(name, out var cliValue))
            {
                value = CoerceCliScalar(cliValue, declaredType);
                source = VariableSource.Cli;
            }
            else if (variablesFile is JsonObject varsObj && varsObj.TryGetPropertyValue(name, out var fromFile))
            {
                value = fromFile?.DeepClone();
                source = VariableSource.File;
            }
            else if (def.DefaultValue is not null)
            {
                value = GraphQLValueCoercer.ToJsonNode(def.DefaultValue, new Dictionary<string, JsonNode?>(StringComparer.Ordinal));
                source = VariableSource.Default;
            }

            if (source == VariableSource.Missing)
            {
                if (IsNonNull(declaredType))
                {
                    throw new ArgumentException($"Required variable '${name}' was not supplied.");
                }

                result[name] = null;

                continue;
            }

            if (value is null && IsNonNull(declaredType))
            {
                throw new ArgumentException($"Variable '${name}' was set to null but its declared type is non-null.");
            }

            result[name] = value;
        }

        return result;
    }

    private static bool IsNonNull(GraphQLType type)
        => type is GraphQLNonNullType;

    private static GraphQLType UnwrapNonNull(GraphQLType type)
        => type is GraphQLNonNullType nn ? nn.Type : type;

    private static JsonValue? CoerceCliScalar(string raw, GraphQLType declaredType)
    {
        var underlying = UnwrapNonNull(declaredType);

        if (underlying is GraphQLListType)
        {
            // CLI --var cannot express lists; users must use --variables-file for lists.
            throw new ArgumentException(
                "List-typed variables cannot be supplied via --var; use --variables-file instead.");
        }

        if (underlying is not GraphQLNamedType named)
        {
            return JsonValue.Create(raw);
        }

        var typeName = named.Name.StringValue;

        if (string.Equals(raw, "null", StringComparison.Ordinal))
        {
            return null;
        }

        return typeName switch
        {
            "Int" when int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i)    => JsonValue.Create(i),
            "Int"                                                                                          => throw new ArgumentException($"Variable value '{raw}' is not a valid Int."),
            "Float" when double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) => JsonValue.Create(d),
            "Float"                                                                                        => throw new ArgumentException($"Variable value '{raw}' is not a valid Float."),
            "Boolean" when bool.TryParse(raw, out var b)                                                   => JsonValue.Create(b),
            "Boolean"                                                                                      => throw new ArgumentException($"Variable value '{raw}' is not a valid Boolean."),
            _                                                                                              => JsonValue.Create(raw)
        };
    }

    /// <summary>
    ///     Parses the textual contents of a <c>--variables-file</c> into a <see cref="JsonNode" />
    ///     suitable for passing to <see cref="Coerce" />. Returns <c>null</c> for empty input.
    /// </summary>
    public static JsonNode? ParseVariablesFile(string contents)
    {
        if (string.IsNullOrWhiteSpace(contents))
        {
            return null;
        }

        using var doc = JsonDocument.Parse(contents);

        return JsonNode.Parse(doc.RootElement.GetRawText());
    }

    private enum VariableSource
    {
        Missing,
        Cli,
        File,
        Default
    }
}