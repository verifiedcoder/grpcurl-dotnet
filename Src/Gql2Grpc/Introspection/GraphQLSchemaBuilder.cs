using System.Text.Json.Nodes;
using Gql2Grpc.Configuration;
using Gql2Grpc.GraphQL;
using Google.Protobuf.Reflection;
using GrpCurl.Net.DescriptorSources;

namespace Gql2Grpc.Introspection;

/// <summary>
/// Synthesises a GraphQL <c>__Schema</c> object from a protobuf descriptor set plus the mapping
/// config. The output is a <see cref="JsonObject"/> ready to be returned as the value of a
/// <c>__schema</c> introspection selection. The built schema is cached per-instance, so callers
/// should create one builder per operation invocation.
/// </summary>
/// <remarks>
/// Constructs a builder that derives the GraphQL schema from <paramref name="source"/>'s
/// descriptor set and applies type-name overrides from <paramref name="config"/>'s
/// <see cref="MappingDefaults.Introspection"/>.
/// </remarks>
public sealed class GraphQLSchemaBuilder(IDescriptorSource source, MappingConfig config)
{
    private readonly IDescriptorSource _source = source;
    private readonly MappingConfig _config = config;
    private readonly IReadOnlyDictionary<string, string> _typeOverrides = config.Defaults.Introspection.TypeOverrides;

    private JsonArray? _cachedTypes;
    private readonly Dictionary<string, JsonObject> _typesByName = new(StringComparer.Ordinal);
    private static readonly string[] BuiltInScalarNames =
    [
        TypeMappings.StringTypeName,
        TypeMappings.IntTypeName,
        TypeMappings.FloatTypeName,
        TypeMappings.BooleanTypeName,
        TypeMappings.IdTypeName
    ];
    private static readonly string[] ExecutableDirectiveLocations = ["FIELD", "FRAGMENT_SPREAD", "INLINE_FRAGMENT"];

    /// <summary>
    /// Builds (or returns the cached) <c>__Schema</c> object, including all types, root operation
    /// type pointers, and the standard set of GraphQL directives.
    /// </summary>
    public JsonObject BuildSchema()
    {
        var types = BuildAllTypes();

        var schema = new JsonObject
        {
            ["__typename"] = "__Schema",
            ["description"] = _config.Defaults.Introspection.SchemaName is { } n ? $"Gql2Grpc synthesised schema for {n}" : null,
            ["queryType"] = TypeRef("Query"),
            ["mutationType"] = HasOperationsOfType(GraphQLOperationType.Mutation) ? TypeRef("Mutation") : null,
            ["subscriptionType"] = HasOperationsOfType(GraphQLOperationType.Subscription) ? TypeRef("Subscription") : null,
            ["types"] = types,
            ["directives"] = BuildDirectives()
        };

        return schema;
    }

    /// <summary>
    /// Looks up a single type by GraphQL name, returning a deep-cloned <see cref="JsonObject"/>
    /// or <c>null</c> when not found. Triggers a one-time schema build on first call.
    /// </summary>
    public JsonObject? FindType(string name)
    {
        _ = BuildAllTypes();
        return _typesByName.TryGetValue(name, out var type) ? (JsonObject)type.DeepClone()! : null;
    }

    private JsonArray BuildAllTypes()
    {
        if (_cachedTypes is not null)
        {
            return (JsonArray)_cachedTypes.DeepClone()!;
        }

        var types = new JsonArray();

        foreach (var scalar in BuiltInScalarNames)
        {
            AppendType(types, ScalarType(scalar, $"Built-in GraphQL scalar {scalar}."));
        }

        foreach (var custom in TypeMappings.CustomScalars)
        {
            AppendType(types, ScalarType(custom, $"Gql2Grpc custom scalar {custom}."));
        }

        AppendType(types, ObjectType("Query", null, BuildRootFields(GraphQLOperationType.Query), []));

        if (HasOperationsOfType(GraphQLOperationType.Mutation))
        {
            AppendType(types, ObjectType("Mutation", null, BuildRootFields(GraphQLOperationType.Mutation), []));
        }

        if (HasOperationsOfType(GraphQLOperationType.Subscription))
        {
            AppendType(types, ObjectType("Subscription", null, BuildRootFields(GraphQLOperationType.Subscription), []));
        }

        if (_source.FileDescriptorSet is { } descriptorSet)
        {
            foreach (var file in descriptorSet.File)
            {
                AddTypesFromProto(types, file);
            }
        }

        AddIntrospectionTypes(types);

        _cachedTypes = types;
        _typesByName.Clear();

        foreach (var node in _cachedTypes)
        {
            if (node is JsonObject typeObj && typeObj["name"] is JsonValue nameVal && nameVal.TryGetValue(out string? typeName) && typeName is not null)
            {
                _typesByName[typeName] = typeObj;
            }
        }

        return (JsonArray)_cachedTypes.DeepClone()!;
    }

    private void AddTypesFromProto(JsonArray types, FileDescriptorProto file)
    {
        foreach (var message in file.MessageType)
        {
            AddMessageRecursive(types, message, file.Package);
        }

        foreach (var enumType in file.EnumType)
        {
            AppendType(types, BuildEnumType(enumType, file.Package));
        }
    }

    private void AddMessageRecursive(JsonArray types, DescriptorProto message, string package)
    {
        var fullName = string.IsNullOrEmpty(package) ? message.Name : $"{package}.{message.Name}";

        if (TypeMappings.TryGetWellKnownScalar(fullName, out _))
        {
            return;
        }

        var name = _typeOverrides.TryGetValue(fullName, out var overridden) ? overridden : message.Name;

        var fields = new List<JsonObject>(message.Field.Count);
        var inputFields = new List<JsonObject>(message.Field.Count);

        foreach (var field in message.Field)
        {
            var typeRef = BuildFieldTypeRef(field);
            var fieldJson = FieldDefinition(field.JsonName, null, [], typeRef);
            fields.Add(fieldJson);
            inputFields.Add(InputValue(field.JsonName, null, typeRef, null));
        }

        AppendType(types, ObjectType(name, fullName, fields, []));
        AppendType(types, InputObjectType(name + "Input", $"Input form of {name}.", inputFields));

        foreach (var nested in message.NestedType)
        {
            AddMessageRecursive(types, nested, fullName);
        }

        foreach (var nestedEnum in message.EnumType)
        {
            AppendType(types, BuildEnumType(nestedEnum, fullName));
        }
    }

    private JsonObject BuildEnumType(EnumDescriptorProto enumProto, string parent)
    {
        var fullName = string.IsNullOrEmpty(parent) ? enumProto.Name : $"{parent}.{enumProto.Name}";
        var name = _typeOverrides.TryGetValue(fullName, out var overridden) ? overridden : enumProto.Name;

        var values = new JsonArray();

        foreach (var value in enumProto.Value)
        {
            values.Add(new JsonObject
            {
                ["name"] = value.Name,
                ["description"] = null,
                ["isDeprecated"] = false,
                ["deprecationReason"] = null
            });
        }

        return new JsonObject
        {
            ["kind"] = "ENUM",
            ["name"] = name,
            ["description"] = fullName,
            ["fields"] = null,
            ["inputFields"] = null,
            ["interfaces"] = null,
            ["enumValues"] = values,
            ["possibleTypes"] = null
        };
    }

    private JsonObject BuildFieldTypeRef(FieldDescriptorProto field)
    {
        JsonObject inner;

        switch (field.Type)
        {
            case FieldDescriptorProto.Types.Type.Message:
                {
                    var (FullyQualified, GraphQLName) = ResolveTypeNameFromProtoRef(field.TypeName);

                    if (TypeMappings.TryGetWellKnownScalar(FullyQualified, out var scalar))
                    {
                        inner = TypeRef(scalar, "SCALAR");
                    }
                    else
                    {
                        inner = TypeRef(GraphQLName, "OBJECT");
                    }

                    break;
                }

            case FieldDescriptorProto.Types.Type.Enum:
                {
                    var (_, GraphQLName) = ResolveTypeNameFromProtoRef(field.TypeName);
                    inner = TypeRef(GraphQLName, "ENUM");
                    break;
                }

            default:
                inner = TypeRef(TypeMappings.ScalarFor(MapType(field.Type)), "SCALAR");
                break;
        }

        if (field.Label == FieldDescriptorProto.Types.Label.Repeated)
        {
            return ListOf(NonNull(inner));
        }

        return inner;
    }

    private (string FullyQualified, string GraphQLName) ResolveTypeNameFromProtoRef(string typeName)
    {
        var trimmed = typeName.StartsWith('.') ? typeName[1..] : typeName;
        var simpleName = trimmed.Contains('.') ? trimmed[(trimmed.LastIndexOf('.') + 1)..] : trimmed;
        var graphQlName = _typeOverrides.TryGetValue(trimmed, out var overridden) ? overridden : simpleName;
        return (trimmed, graphQlName);
    }

    private static FieldType MapType(FieldDescriptorProto.Types.Type type) => type switch
    {
        FieldDescriptorProto.Types.Type.String => FieldType.String,
        FieldDescriptorProto.Types.Type.Bool => FieldType.Bool,
        FieldDescriptorProto.Types.Type.Int32 => FieldType.Int32,
        FieldDescriptorProto.Types.Type.Int64 => FieldType.Int64,
        FieldDescriptorProto.Types.Type.Uint32 => FieldType.UInt32,
        FieldDescriptorProto.Types.Type.Uint64 => FieldType.UInt64,
        FieldDescriptorProto.Types.Type.Sint32 => FieldType.SInt32,
        FieldDescriptorProto.Types.Type.Sint64 => FieldType.SInt64,
        FieldDescriptorProto.Types.Type.Float => FieldType.Float,
        FieldDescriptorProto.Types.Type.Double => FieldType.Double,
        FieldDescriptorProto.Types.Type.Fixed32 => FieldType.Fixed32,
        FieldDescriptorProto.Types.Type.Fixed64 => FieldType.Fixed64,
        FieldDescriptorProto.Types.Type.Sfixed32 => FieldType.SFixed32,
        FieldDescriptorProto.Types.Type.Sfixed64 => FieldType.SFixed64,
        FieldDescriptorProto.Types.Type.Bytes => FieldType.Bytes,
        _ => FieldType.String
    };

    private IEnumerable<JsonObject> BuildRootFields(GraphQLOperationType operationType)
    {
        foreach (var entry in _config.Operations)
        {
            if (entry.OperationType != operationType)
            {
                continue;
            }

            yield return FieldDefinition(
                entry.GraphqlField,
                $"Maps to {entry.Service ?? _config.Defaults.Service ?? "(service)"}/{entry.Method}",
                BuildEntryArguments(entry),
                TypeRef("JsonScalar", "SCALAR"));
        }
    }

    private static List<JsonObject> BuildEntryArguments(MappingEntry entry)
    {
        var list = new List<JsonObject>();

        foreach (var (argName, rule) in entry.Arguments)
        {
            if (string.Equals(argName, "$selection", StringComparison.Ordinal))
            {
                continue;
            }

            if (rule is ArgumentRule.Literal or ArgumentRule.SkipArgument)
            {
                continue;
            }

            list.Add(InputValue(argName, null, TypeRef(TypeMappings.StringTypeName, "SCALAR"), null));
        }

        return list;
    }

    private bool HasOperationsOfType(GraphQLOperationType type) =>
        _config.Operations.Any(entry => entry.OperationType == type);

    private static void AddIntrospectionTypes(JsonArray types)
    {
        AppendType(types, ScalarType("__TypeKind", "Enum-like scalar for introspection kinds."));

        AppendType(types, ObjectType("__Schema", null,
        [
            FieldDefinition("description", null, [], TypeRef(TypeMappings.StringTypeName, "SCALAR")),
            FieldDefinition("queryType", null, [], NonNull(TypeRef("__Type", "OBJECT"))),
            FieldDefinition("mutationType", null, [], TypeRef("__Type", "OBJECT")),
            FieldDefinition("subscriptionType", null, [], TypeRef("__Type", "OBJECT")),
            FieldDefinition("types", null, [], NonNull(ListOf(NonNull(TypeRef("__Type", "OBJECT"))))),
            FieldDefinition("directives", null, [], NonNull(ListOf(NonNull(TypeRef("__Directive", "OBJECT")))))
        ], []));

        AppendType(types, ObjectType("__Type", null,
        [
            FieldDefinition("kind", null, [], NonNull(TypeRef("__TypeKind", "SCALAR"))),
            FieldDefinition("name", null, [], TypeRef(TypeMappings.StringTypeName, "SCALAR")),
            FieldDefinition("description", null, [], TypeRef(TypeMappings.StringTypeName, "SCALAR")),
            FieldDefinition("fields", null, [], ListOf(NonNull(TypeRef("__Field", "OBJECT")))),
            FieldDefinition("inputFields", null, [], ListOf(NonNull(TypeRef("__InputValue", "OBJECT")))),
            FieldDefinition("interfaces", null, [], ListOf(NonNull(TypeRef("__Type", "OBJECT")))),
            FieldDefinition("enumValues", null, [], ListOf(NonNull(TypeRef("__EnumValue", "OBJECT")))),
            FieldDefinition("possibleTypes", null, [], ListOf(NonNull(TypeRef("__Type", "OBJECT")))),
            FieldDefinition("ofType", null, [], TypeRef("__Type", "OBJECT"))
        ], []));

        AppendType(types, ObjectType("__Field", null,
        [
            FieldDefinition("name", null, [], NonNull(TypeRef(TypeMappings.StringTypeName, "SCALAR"))),
            FieldDefinition("description", null, [], TypeRef(TypeMappings.StringTypeName, "SCALAR")),
            FieldDefinition("args", null, [], NonNull(ListOf(NonNull(TypeRef("__InputValue", "OBJECT"))))),
            FieldDefinition("type", null, [], NonNull(TypeRef("__Type", "OBJECT"))),
            FieldDefinition("isDeprecated", null, [], NonNull(TypeRef(TypeMappings.BooleanTypeName, "SCALAR"))),
            FieldDefinition("deprecationReason", null, [], TypeRef(TypeMappings.StringTypeName, "SCALAR"))
        ], []));

        AppendType(types, ObjectType("__InputValue", null,
        [
            FieldDefinition("name", null, [], NonNull(TypeRef(TypeMappings.StringTypeName, "SCALAR"))),
            FieldDefinition("description", null, [], TypeRef(TypeMappings.StringTypeName, "SCALAR")),
            FieldDefinition("type", null, [], NonNull(TypeRef("__Type", "OBJECT"))),
            FieldDefinition("defaultValue", null, [], TypeRef(TypeMappings.StringTypeName, "SCALAR"))
        ], []));

        AppendType(types, ObjectType("__EnumValue", null,
        [
            FieldDefinition("name", null, [], NonNull(TypeRef(TypeMappings.StringTypeName, "SCALAR"))),
            FieldDefinition("description", null, [], TypeRef(TypeMappings.StringTypeName, "SCALAR")),
            FieldDefinition("isDeprecated", null, [], NonNull(TypeRef(TypeMappings.BooleanTypeName, "SCALAR"))),
            FieldDefinition("deprecationReason", null, [], TypeRef(TypeMappings.StringTypeName, "SCALAR"))
        ], []));

        AppendType(types, ObjectType("__Directive", null,
        [
            FieldDefinition("name", null, [], NonNull(TypeRef(TypeMappings.StringTypeName, "SCALAR"))),
            FieldDefinition("description", null, [], TypeRef(TypeMappings.StringTypeName, "SCALAR")),
            FieldDefinition("locations", null, [], NonNull(ListOf(NonNull(TypeRef(TypeMappings.StringTypeName, "SCALAR"))))),
            FieldDefinition("args", null, [], NonNull(ListOf(NonNull(TypeRef("__InputValue", "OBJECT")))))
        ], []));
    }

    private static JsonArray BuildDirectives()
    {
        var directives = new JsonArray
        {
            DirectiveDefinition("include", "Directs the executor to include this field or fragment only when the `if` argument is true.",
                ExecutableDirectiveLocations,
                [InputValue("if", null, NonNull(TypeRef(TypeMappings.BooleanTypeName, "SCALAR")), null)]),
            DirectiveDefinition("skip", "Directs the executor to skip this field or fragment when the `if` argument is true.",
                ExecutableDirectiveLocations,
                [InputValue("if", null, NonNull(TypeRef(TypeMappings.BooleanTypeName, "SCALAR")), null)]),
            DirectiveDefinition("deprecated", "Marks an element of a GraphQL schema as no longer supported.",
                ["FIELD_DEFINITION", "ENUM_VALUE"],
                [InputValue("reason", null, TypeRef(TypeMappings.StringTypeName, "SCALAR"), JsonValue.Create("No longer supported"))])
        };
        return directives;
    }

    private static JsonObject DirectiveDefinition(string name, string description, string[] locations, IEnumerable<JsonObject> args)
    {
        var locArr = new JsonArray();

        foreach (var loc in locations)
        {
            locArr.Add(loc);
        }

        var argsArr = new JsonArray();

        foreach (var arg in args)
        {
            argsArr.Add(arg);
        }

        return new JsonObject
        {
            ["name"] = name,
            ["description"] = description,
            ["locations"] = locArr,
            ["args"] = argsArr
        };
    }

    private static JsonObject ObjectType(string name, string? description, IEnumerable<JsonObject> fields, IReadOnlyList<JsonObject> interfaces)
    {
        var fieldArr = new JsonArray();

        foreach (var f in fields)
        {
            fieldArr.Add(f);
        }

        var interfaceArr = new JsonArray();

        foreach (var i in interfaces)
        {
            interfaceArr.Add(i);
        }

        return new JsonObject
        {
            ["kind"] = "OBJECT",
            ["name"] = name,
            ["description"] = description,
            ["fields"] = fieldArr,
            ["inputFields"] = null,
            ["interfaces"] = interfaceArr,
            ["enumValues"] = null,
            ["possibleTypes"] = null
        };
    }

    private static JsonObject InputObjectType(string name, string? description, IEnumerable<JsonObject> inputFields)
    {
        var arr = new JsonArray();

        foreach (var f in inputFields)
        {
            arr.Add(f);
        }

        return new JsonObject
        {
            ["kind"] = "INPUT_OBJECT",
            ["name"] = name,
            ["description"] = description,
            ["fields"] = null,
            ["inputFields"] = arr,
            ["interfaces"] = null,
            ["enumValues"] = null,
            ["possibleTypes"] = null
        };
    }

    private static JsonObject ScalarType(string name, string? description) => new()
    {
        ["kind"] = "SCALAR",
        ["name"] = name,
        ["description"] = description,
        ["fields"] = null,
        ["inputFields"] = null,
        ["interfaces"] = null,
        ["enumValues"] = null,
        ["possibleTypes"] = null
    };

    private static JsonObject TypeRef(string name, string kind = "OBJECT") => new()
    {
        ["kind"] = kind,
        ["name"] = name,
        ["ofType"] = null
    };

    private static JsonObject NonNull(JsonObject inner) => new()
    {
        ["kind"] = "NON_NULL",
        ["name"] = null,
        ["ofType"] = inner
    };

    private static JsonObject ListOf(JsonObject inner) => new()
    {
        ["kind"] = "LIST",
        ["name"] = null,
        ["ofType"] = inner
    };

    private static JsonObject FieldDefinition(string name, string? description, IReadOnlyList<JsonObject> args, JsonObject type)
    {
        var argsArr = new JsonArray();

        foreach (var a in args)
        {
            argsArr.Add(a);
        }

        return new JsonObject
        {
            ["name"] = name,
            ["description"] = description,
            ["args"] = argsArr,
            ["type"] = type,
            ["isDeprecated"] = false,
            ["deprecationReason"] = null
        };
    }

    private static JsonObject InputValue(string name, string? description, JsonObject type, JsonNode? defaultValue) => new()
    {
        ["name"] = name,
        ["description"] = description,
        ["type"] = type,
        ["defaultValue"] = defaultValue?.DeepClone()
    };

    private static void AppendType(JsonArray types, JsonObject type)
    {
        types.Add(type);
    }
}
