using Google.Protobuf.Reflection;
using System.Text.Json;

namespace GrpCurl.Net.Invocation;

/// <summary>
///     Builds a JSON request-template skeleton (default value per field) for a message descriptor.
///     This is the single shared implementation behind the CLI's <c>describe --msg-template</c> and
///     GrpCurl.Net Studio's "Generate request template" (FR-052), so both emit byte-identical JSON.
///     Well-known types get canonical placeholder forms and recursion is guarded.
/// </summary>
internal static class MessageTemplateGenerator
{
    private static readonly JsonSerializerOptions IndentedOptions = new()
    {
        WriteIndented = true
    };

    /// <summary>The template as a field-name → default-value dictionary, recursion-guarded.</summary>
    public static Dictionary<string, object?> CreateTemplate(MessageDescriptor messageDescriptor)
        => CreateTemplate(messageDescriptor, []);

    /// <summary>The template serialized as canonical indented JSON (matches <c>describe --msg-template</c>).</summary>
    public static string GenerateJson(MessageDescriptor messageDescriptor)
        => JsonSerializer.Serialize(CreateTemplate(messageDescriptor), IndentedOptions);

    /// <summary>
    ///     Creates a JSON template for a message descriptor with recursion detection.
    /// </summary>
    /// <param name="messageDescriptor">The message descriptor to create a template for</param>
    /// <param name="visitedTypes">Set of visited type full names to detect recursion</param>
    /// <returns>Dictionary representing the JSON template</returns>
    internal static Dictionary<string, object?> CreateTemplate(MessageDescriptor messageDescriptor, HashSet<string> visitedTypes)
    {
        var template = new Dictionary<string, object?>();

        // Check for recursion
        if (visitedTypes.Contains(messageDescriptor.FullName))
        {
            // Return a placeholder for recursive types
            template["<recursive>"] = messageDescriptor.FullName;

            return template;
        }

        // Add current type to visited set
        var currentVisited = new HashSet<string>(visitedTypes) { messageDescriptor.FullName };

        foreach (var field in messageDescriptor.Fields.InDeclarationOrder())
        {
            template[field.Name] = GetDefaultValueForField(field, currentVisited);
        }

        return template;
    }

    /// <summary>
    ///     Gets the default template value for a field based on its type.
    /// </summary>
    internal static object? GetDefaultValueForField(FieldDescriptor field, HashSet<string> visitedTypes)
    {
        // Handle repeated fields (arrays)
        if (!field.IsRepeated)
        {
            return field.FieldType switch
            {
                FieldType.Message => HandleWellKnownType(field.MessageType, visitedTypes),
                FieldType.Enum    => GetEnumDefault(field.EnumType),
                _                 => GetScalarDefault(field)
            };
        }

        // Handle non-repeated fields
        // For map fields
        if (field.IsMap)
        {
            var mapTemplate = new Dictionary<string, object?>();
            var mapKeyField = field.MessageType.Fields[1];   // Key field in map entry
            var mapValueField = field.MessageType.Fields[2]; // Value field in map entry
            var keyDefault = GetMapKeyDefault(mapKeyField);

            mapTemplate[keyDefault] = mapValueField.FieldType switch
            {
                FieldType.Message => HandleWellKnownType(mapValueField.MessageType, visitedTypes),
                FieldType.Enum    => GetEnumDefault(mapValueField.EnumType),
                _                 => GetScalarDefault(mapValueField)
            };

            return mapTemplate;
        }

        // For regular repeated fields
        var arrayTemplate = new List<object?>();
        var elementValue = field.FieldType switch
        {
            FieldType.Message => CreateTemplate(field.MessageType, visitedTypes),
            FieldType.Enum    => GetEnumDefault(field.EnumType),
            _                 => GetScalarDefault(field)
        };

        arrayTemplate.Add(elementValue);

        return arrayTemplate;
    }

    /// <summary>
    ///     Handles well-known types with special formatting.
    /// </summary>
    internal static object? HandleWellKnownType(MessageDescriptor messageDescriptor, HashSet<string> visitedTypes)
    {
        // Check for well-known types and provide appropriate defaults
        return messageDescriptor.FullName switch
        {
            "google.protobuf.Timestamp"   => "1970-01-01T00:00:00Z",
            "google.protobuf.Duration"    => "0s",
            "google.protobuf.Int32Value"  => 0,
            "google.protobuf.Int64Value"  => "0",
            "google.protobuf.UInt32Value" => 0,
            "google.protobuf.UInt64Value" => "0",
            "google.protobuf.FloatValue"  => 0,
            "google.protobuf.DoubleValue" => 0,
            "google.protobuf.BoolValue"   => false,
            "google.protobuf.StringValue" => "",
            "google.protobuf.BytesValue"  => null,
            "google.protobuf.Empty"       => new Dictionary<string, object?>(),
            "google.protobuf.Struct"      => new Dictionary<string, object?> { ["google.protobuf.Struct"] = "supports arbitrary JSON objects" },
            "google.protobuf.Value"       => new Dictionary<string, object?> { ["google.protobuf.Value"] = "supports arbitrary JSON" },
            "google.protobuf.ListValue"   => new List<object?> { new Dictionary<string, object?> { ["google.protobuf.ListValue"] = "is an array of arbitrary JSON values" } },
            "google.protobuf.Any"         => new Dictionary<string, object?> { ["@type"] = "type.googleapis.com/google.protobuf.Empty", ["value"] = new Dictionary<string, object?>() },
            "google.protobuf.FieldMask"   => new Dictionary<string, object?> { ["paths"] = new List<object?> { "" } },
            _                             => CreateTemplate(messageDescriptor, visitedTypes)
        };
    }

    /// <summary>
    ///     Gets the default value for an enum field.
    /// </summary>
    /// <remarks>Return the first enum value name (usually the zero value).</remarks>
    internal static string GetEnumDefault(EnumDescriptor enumDescriptor)
        => enumDescriptor.Values.Count > 0 ? enumDescriptor.Values[0].Name : "UNKNOWN";

    /// <summary>
    ///     Gets the default value for a scalar field.
    /// </summary>
    internal static object? GetScalarDefault(FieldDescriptor field)
        => field.FieldType switch
        {
            FieldType.Double   => 0,
            FieldType.Float    => 0,
            FieldType.Int32    => 0,
            FieldType.Int64    => "0",
            FieldType.UInt32   => 0,
            FieldType.UInt64   => "0",
            FieldType.SInt32   => 0,
            FieldType.SInt64   => "0",
            FieldType.Fixed32  => 0,
            FieldType.Fixed64  => "0",
            FieldType.SFixed32 => 0,
            FieldType.SFixed64 => "0",
            FieldType.Bool     => false,
            FieldType.String   => "",
            FieldType.Bytes    => "",
            _                  => null
        };

    /// <summary>
    ///     Gets the default key string for a map key field to match Go grpcurl.
    /// </summary>
    internal static string GetMapKeyDefault(FieldDescriptor keyField)
        => keyField.FieldType switch
        {
            FieldType.String                                          => "",
            FieldType.Bool                                            => "false",
            FieldType.Int32 or FieldType.SInt32 or FieldType.SFixed32 => "0",
            FieldType.Int64 or FieldType.SInt64 or FieldType.SFixed64 => "0",
            FieldType.UInt32 or FieldType.Fixed32                     => "0",
            FieldType.UInt64 or FieldType.Fixed64                     => "0",
            _                                                         => ""
        };
}
