using Gql2Grpc.GraphQL;
using GrpCurl.Net.Utilities;
using System.Text.Json;
using System.Text.Json.Nodes;
using YamlDotNet.Serialization;

namespace Gql2Grpc.Configuration;

/// <summary>
///     Loads a <see cref="MappingConfig" /> from a YAML or JSON file path. Format is detected from the
///     extension (<c>.yaml</c>/<c>.yml</c> → YAML, <c>.json</c> → JSON; everything else tries YAML then
///     JSON). Literal values are expanded for <c>${ENV_VAR}</c> references at load time, reusing
///     <see cref="GrpcChannelFactory.ExpandEnvironmentVariables" />.
/// </summary>
public static class MappingConfigLoader
{
    /// <summary>
    ///     Loads and parses a mapping configuration from <paramref name="path" />. Returns
    ///     <see cref="MappingConfig.Empty" /> when <paramref name="path" /> is <c>null</c> or empty.
    /// </summary>
    /// <param name="path">Path to a YAML or JSON mapping file (extension-detected).</param>
    /// <param name="cancellationToken">Cancellation token applied to the async file read.</param>
    /// <exception cref="FileNotFoundException">Thrown when <paramref name="path" /> is set but does not exist.</exception>
    /// <exception cref="InvalidDataException">Thrown when the file does not contain a top-level object/map.</exception>
    public static async Task<MappingConfig> LoadAsync(string? path, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(path))
        {
            return MappingConfig.Empty;
        }

        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"Mapping file not found: {path}", path);
        }

        var text = await InputFileGuard.ReadAllTextAsync(
            path,
            InputFileGuard.MaxMappingConfigBytes,
            "Mapping configuration file",
            cancellationToken).ConfigureAwait(false);
        var extension = Path.GetExtension(path).ToLowerInvariant();

        var root = extension switch
        {
            ".yaml" or ".yml" => ParseYaml(text),
            ".json"           => JsonNode.Parse(text),
            _                 => TryParseAny(text)
        };

        return root is not JsonObject rootObject
            ? throw new InvalidDataException("Mapping file must contain a top-level object/map.")
            : FromJson(rootObject);
    }

    /// <summary>
    ///     Parses an inline mapping document (YAML or JSON, auto-detected) into a
    ///     <see cref="MappingConfig" />. Returns <see cref="MappingConfig.Empty" /> for null/blank text.
    ///     For hosts that edit the mapping in-memory (e.g. Studio's inline mapping buffer) rather than
    ///     from a file; the same load-time validation as <see cref="LoadAsync" /> applies.
    /// </summary>
    /// <param name="text">The mapping document text (YAML or JSON).</param>
    /// <exception cref="InvalidDataException">Thrown when the text is not a top-level object/map or is invalid.</exception>
    public static MappingConfig FromText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return MappingConfig.Empty;
        }

        return TryParseAny(text) is not JsonObject rootObject
            ? throw new InvalidDataException("Mapping must contain a top-level object/map.")
            : FromJson(rootObject);
    }

    /// <summary>Parses an already-deserialized JSON object into a <see cref="MappingConfig" />.</summary>
    /// <param name="root">JSON object containing the mapping configuration shape.</param>
    /// <exception cref="InvalidDataException">Thrown when required fields are missing or invalid.</exception>
    public static MappingConfig FromJson(JsonObject root)
    {
        var version = GetInt(root, "version") ?? 1;
        var defaults = ReadDefaults(root["defaults"] as JsonObject);
        var operations = ReadOperations(root["operations"] as JsonArray);

        ValidateNoDuplicates(operations);

        return new MappingConfig
        {
            Version = version,
            Defaults = defaults,
            Operations = operations
        };
    }

    private static JsonNode? ParseYaml(string text)
    {
        var deserializer = new DeserializerBuilder().Build();
        var value = deserializer.Deserialize<object?>(text);

        return YamlToJson(value);
    }

    private static JsonNode? TryParseAny(string text)
    {
        try
        {
            return JsonNode.Parse(text);
        }
        catch (JsonException)
        {
            return ParseYaml(text);
        }
    }

    private static JsonNode? YamlToJson(object? node)
    {
        switch (node)
        {
            case null:

                return null;

            case string s:

                return JsonValue.Create(ExpandLiteral(s));

            case bool b:

                return JsonValue.Create(b);

            case int i:

                return JsonValue.Create(i);

            case long l:

                return JsonValue.Create(l);

            case double d:

                return JsonValue.Create(d);

            case IDictionary<object, object?> dict:

            {
                var obj = new JsonObject();

                foreach (var (k, v) in dict)
                {
                    obj[k.ToString()!] = YamlToJson(v);
                }

                return obj;
            }

            case IEnumerable<object?> list:

            {
                var arr = new JsonArray();

                foreach (var item in list)
                {
                    arr.Add(YamlToJson(item));
                }

                return arr;
            }

            default:

                return JsonValue.Create(node.ToString());
        }
    }

    private static string ExpandLiteral(string value) => !value.Contains("${", StringComparison.Ordinal)
        ? value
        : GrpcChannelFactory.ExpandEnvironmentVariables(value, value);

    private static MappingDefaults ReadDefaults(JsonObject? defaults)
    {
        if (defaults is null)
        {
            return new MappingDefaults();
        }

        return new MappingDefaults
        {
            Service = GetString(defaults, "service"),
            ArgumentAliases = ReadStringMap(defaults["argumentAliases"] as JsonObject),
            Convention = ReadConvention(defaults["convention"] as JsonObject),
            Introspection = ReadIntrospection(defaults["introspection"] as JsonObject)
        };
    }

    private static MappingConvention ReadConvention(JsonObject? convention)
    {
        if (convention is null)
        {
            return new MappingConvention();
        }

        return new MappingConvention
        {
            ListMethodPrefix = GetString(convention, "listMethodPrefix") ?? string.Empty,
            PascalCaseFieldNames = GetBool(convention, "pascalCaseFieldNames") ?? true
        };
    }

    private static IntrospectionDefaults ReadIntrospection(JsonObject? introspection)
    {
        if (introspection is null)
        {
            return new IntrospectionDefaults();
        }

        return new IntrospectionDefaults
        {
            SchemaName = GetString(introspection, "schemaName"),
            TypeOverrides = ReadStringMap(introspection["typeOverrides"] as JsonObject)
        };
    }

    private static List<MappingEntry> ReadOperations(JsonArray? operations)
    {
        if (operations is null || operations.Count == 0)
        {
            return [];
        }

        var list = new List<MappingEntry>(operations.Count);

        foreach (var node in operations)
        {
            if (node is not JsonObject entry)
            {
                throw new InvalidDataException("Every item in 'operations' must be an object.");
            }

            list.Add(ReadEntry(entry));
        }

        return list;
    }

    private static MappingEntry ReadEntry(JsonObject entry)
    {
        var graphqlField = GetString(entry, "graphqlField")
                           ?? throw new InvalidDataException("Every operations entry must declare 'graphqlField'.");

        var method = GetString(entry, "method")
                     ?? throw new InvalidDataException($"Operations entry '{graphqlField}' must declare 'method'.");

        var operationType = ParseOperationType(GetString(entry, "operationType"), graphqlField);
        var kind = ParseMethodKind(GetString(entry, "kind"), operationType);

        var (arguments, selectionFieldMaskPath) = ReadArguments(entry["arguments"] as JsonObject, graphqlField);

        return new MappingEntry
        {
            GraphqlField = graphqlField,
            OperationType = operationType,
            Service = GetString(entry, "service"),
            Method = method,
            Kind = kind,
            Arguments = arguments,
            Response = ReadResponseShaping(entry["response"] as JsonObject),
            SelectionFieldMaskPath = selectionFieldMaskPath
        };
    }

    private static (IReadOnlyDictionary<string, ArgumentRule>, string? SelectionFieldMaskPath) ReadArguments(
        JsonObject? arguments,
        string graphqlField)
    {
        if (arguments is null || arguments.Count == 0)
        {
            return (new Dictionary<string, ArgumentRule>(StringComparer.Ordinal), null);
        }

        var result = new Dictionary<string, ArgumentRule>(StringComparer.Ordinal);
        string? selectionMaskPath = null;

        foreach (var (name, valueNode) in arguments)
        {
            if (string.Equals(name, "$selection", StringComparison.Ordinal))
            {
                if (valueNode is not JsonObject specialObj)
                {
                    throw new InvalidDataException(
                        $"Operations entry '{graphqlField}' has invalid $selection rule; expected an object with a 'fieldMask' key.");
                }

                selectionMaskPath = GetString(specialObj, "fieldMask")
                                    ?? throw new InvalidDataException(
                                        $"Operations entry '{graphqlField}' has $selection without a 'fieldMask' target path.");

                continue;
            }

            result[name] = ReadArgumentRule(name, valueNode, graphqlField);
        }

        return (result, selectionMaskPath);
    }

    private static ArgumentRule ReadArgumentRule(string argName, JsonNode? node, string graphqlField)
    {
        switch (node)
        {
            case JsonValue v when v.TryGetValue(out string? scalar):

                return new ArgumentRule.Rename(scalar);

            case JsonObject obj:

                if (GetBool(obj, "skip") == true)
                {
                    return new ArgumentRule.SkipArgument();
                }

                if (GetString(obj, "path") is { } path)
                {
                    return new ArgumentRule.PathRule(path);
                }

                if (GetString(obj, "literal") is { } literal)
                {
                    return new ArgumentRule.Literal(literal);
                }

                if (GetString(obj, "rename") is { } rename)
                {
                    return new ArgumentRule.Rename(rename);
                }

                throw new InvalidDataException(
                    $"Operations entry '{graphqlField}' argument '{argName}' has an unrecognised rule shape.");

            default:

                throw new InvalidDataException(
                    $"Operations entry '{graphqlField}' argument '{argName}' must be a string or object.");
        }
    }

    private static ResponseShaping? ReadResponseShaping(JsonObject? response)
    {
        if (response is null)
        {
            return null;
        }

        return new ResponseShaping
        {
            Unwrap = GetString(response, "unwrap")
        };
    }

    private static GraphQLOperationType ParseOperationType(string? raw, string fieldName)
    {
        return (raw ?? "query").ToLowerInvariant() switch
        {
            "query"        => GraphQLOperationType.Query,
            "mutation"     => GraphQLOperationType.Mutation,
            "subscription" => GraphQLOperationType.Subscription,
            _ => throw new InvalidDataException(
                $"Operations entry '{fieldName}' has unknown operationType '{raw}'.")
        };
    }

    private static MethodKind ParseMethodKind(string? raw, GraphQLOperationType operationType)
    {
        if (raw is null)
        {
            return operationType == GraphQLOperationType.Subscription ? MethodKind.ServerStreaming : MethodKind.Unary;
        }

        return raw.ToLowerInvariant() switch
        {
            "unary"                                                       => MethodKind.Unary,
            "serverstreaming" or "server_streaming" or "server-streaming" => MethodKind.ServerStreaming,
            _                                                             => throw new InvalidDataException($"Unknown method kind '{raw}'.")
        };
    }

    private static void ValidateNoDuplicates(IReadOnlyList<MappingEntry> entries)
    {
        var seen = new HashSet<(string, GraphQLOperationType)>();

        foreach (var entry in entries)
        {
            var key = (entry.GraphqlField, entry.OperationType);

            if (!seen.Add(key))
            {
                throw new InvalidDataException($"Duplicate mapping for ({entry.GraphqlField}, {entry.OperationType}).");
            }
        }
    }

    private static Dictionary<string, string> ReadStringMap(JsonObject? map)
    {
        if (map is null || map.Count == 0)
        {
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }

        var result = new Dictionary<string, string>(map.Count, StringComparer.Ordinal);

        foreach (var (k, v) in map)
        {
            if (v is JsonValue vv && vv.TryGetValue(out string? s))
            {
                result[k] = s;
            }
        }

        return result;
    }

    private static string? GetString(JsonObject obj, string key)
        => obj[key] is JsonValue v && v.TryGetValue(out string? s)
            ? s
            : null;

    private static bool? GetBool(JsonObject obj, string key)
    {
        if (obj[key] is not JsonValue v)
        {
            return null;
        }

        if (v.TryGetValue(out bool b))
        {
            return b;
        }

        if (v.TryGetValue(out string? s) && bool.TryParse(s, out var parsed))
        {
            return parsed;
        }

        return null;
    }

    private static int? GetInt(JsonObject obj, string key)
    {
        if (obj[key] is not JsonValue v)
        {
            return null;
        }

        if (v.TryGetValue(out int i))
        {
            return i;
        }

        if (v.TryGetValue(out string? s) && int.TryParse(s, out var parsed))
        {
            return parsed;
        }

        return null;
    }
}
