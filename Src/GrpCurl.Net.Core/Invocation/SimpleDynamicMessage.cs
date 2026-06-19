using Google.Protobuf;
using Google.Protobuf.Reflection;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace GrpCurl.Net.Invocation;

/// <summary>
///     Simple dynamic message implementation for runtime message creation
/// </summary>
internal class SimpleDynamicMessage : IMessage
{
    // List to track unknown fields encountered during JSON parsing
    private readonly List<string> _unknownFields = [];

    // The unknown-field policy in force while this message is being populated. Threaded
    // into nested/repeated/map/Any sub-messages so strict mode rejects unknown fields at
    // any depth (not just the top level) and lenient mode tracks them with a dotted path.
    private bool _allowUnknownFields = true;
    internal readonly Dictionary<FieldDescriptor, object?> Fields = [];
    internal readonly Dictionary<FieldDescriptor, Dictionary<object, object?>> MapFields = [];

    // Track which field is set in each oneof (OneofDescriptor -> active FieldDescriptor)
    internal readonly Dictionary<OneofDescriptor, FieldDescriptor?> OneofFields = [];
    internal readonly Dictionary<FieldDescriptor, List<object?>> RepeatedFields = [];

    // Constructor for creating empty message (for deserialization)
    public SimpleDynamicMessage(MessageDescriptor descriptor) => Descriptor = descriptor;

    // Constructor for creating message from JSON (for request serialization)
    public SimpleDynamicMessage(MessageDescriptor descriptor, string? json, bool allowUnknownFields = true)
    {
        Descriptor = descriptor;

        // Parse JSON and populate fields
        if (json is null)
        {
            return;
        }

        using var jsonDoc = JsonDocument.Parse(json);

        PopulateFromJsonObject(jsonDoc.RootElement, allowUnknownFields);
    }

    /// <summary>
    ///     Populates this message's fields from a JSON object element. Shared by the JSON-string
    ///     constructor and the <c>google.protobuf.Any</c> embedded-message path so both honour the
    ///     same field-matching and unknown-field rules.
    /// </summary>
    private void PopulateFromJsonObject(JsonElement root, bool allowUnknownFields)
    {
        _allowUnknownFields = allowUnknownFields;

        foreach (var property in root.EnumerateObject())
        {
            var field = Descriptor.Fields.InDeclarationOrder().FirstOrDefault(f =>
                                                                                  f.JsonName.Equals(property.Name, StringComparison.OrdinalIgnoreCase) ||
                                                                                  f.Name.Equals(property.Name, StringComparison.OrdinalIgnoreCase));

            if (field is null)
            {
                // Track unknown field
                _unknownFields.Add(property.Name);

                if (!allowUnknownFields)
                {
                    throw new ArgumentException($"Unknown field '{property.Name}' in message type '{Descriptor.FullName}'. Use --allow-unknown-fields to skip unknown fields.");
                }

                continue;
            }

            if (field.IsMap && property.Value.ValueKind == JsonValueKind.Object)
            {
                // Handle map field (JSON object)
                MapFields[field] = [];

                var mapDescriptor = field.MessageType;
                var keyField = mapDescriptor.FindFieldByNumber(1);
                var valueField = mapDescriptor.FindFieldByNumber(2);

                foreach (var mapEntry in property.Value.EnumerateObject())
                {
                    // Convert the key based on the key field type
                    var key = ConvertMapKey(mapEntry.Name, keyField);

                    // Convert the value based on the value field type
                    var valueElement = mapEntry.Value;
                    var value = ConvertJsonValue(valueElement, valueField);

                    MapFields[field][key] = value;
                }
            }
            else if (field.IsRepeated && property.Value.ValueKind == JsonValueKind.Array)
            {
                // Handle repeated field (array)
                RepeatedFields[field] = [];

                foreach (var element in property.Value.EnumerateArray())
                {
                    // Protocol Buffers do not support null elements in repeated fields
                    if (element.ValueKind == JsonValueKind.Null)
                    {
                        throw new ArgumentException(
                            $"Null values are not allowed in repeated field '{field.Name}'. " +
                            "Protocol Buffers do not support null in repeated fields.");
                    }

                    var value = ConvertJsonValue(element, field);

                    RepeatedFields[field].Add(value);
                }
            }
            else
            {
                // Handle regular field (and oneof fields)
                var value = ConvertJsonValue(property.Value, field);

                // If this field is part of oneof, clear other fields in the same oneof
                if (field.ContainingOneof is { IsSynthetic: false })
                {
                    var oneof = field.ContainingOneof;

                    // Clear any other field in this oneof
                    oneof.Fields
                         .Where(f => f != field)
                         .ToList()
                         .ForEach(f => Fields.Remove(f));

                    // Track which field is active in this oneof
                    OneofFields[oneof] = field;
                }

                Fields[field] = value;
            }
        }
    }

    /// <summary>
    ///     Gets the list of unknown fields encountered during JSON parsing.
    /// </summary>
    public IReadOnlyList<string> UnknownFields
        => _unknownFields.AsReadOnly();

    public MessageDescriptor Descriptor { get; }

    public void WriteTo(CodedOutputStream output)
        => ProtobufWriter.WriteTo(this, output);

    public int CalculateSize()
        => ProtobufWriter.CalculateSize(this);

    public void MergeFrom(CodedInputStream input)
        => ProtobufReader.MergeFrom(this, input);

    private object? ConvertJsonValue(JsonElement element, FieldDescriptor field)
    {
        if (element.ValueKind == JsonValueKind.Null)
        {
            // Only message types can be null in protobuf; scalars use defaults
            return field.FieldType == FieldType.Message ? null : GetDefaultValue(field);
        }

        return field.FieldType switch
        {
            FieldType.String                                          => element.GetString(),
            FieldType.Int32 or FieldType.SInt32 or FieldType.SFixed32 => element.GetInt32(),
            FieldType.Int64 or FieldType.SInt64 or FieldType.SFixed64 =>
                element.ValueKind == JsonValueKind.String
                    ? long.Parse(element.GetString()!)
                    : element.GetInt64(),
            FieldType.UInt32 or FieldType.Fixed32 => element.GetUInt32(),
            FieldType.UInt64 or FieldType.Fixed64 =>
                element.ValueKind == JsonValueKind.String
                    ? ulong.Parse(element.GetString()!)
                    : element.GetUInt64(),
            FieldType.Bool => element.GetBoolean(),
            FieldType.Float => element.ValueKind == JsonValueKind.String
                ? element.GetString() switch
                {
                    "NaN"       => float.NaN,
                    "Infinity"  => float.PositiveInfinity,
                    "-Infinity" => float.NegativeInfinity,
                    var s       => throw new ArgumentException($"Invalid float value: {s}")
                }
                : (float)element.GetDouble(),
            FieldType.Double => element.ValueKind == JsonValueKind.String
                ? element.GetString() switch
                {
                    "NaN"       => double.NaN,
                    "Infinity"  => double.PositiveInfinity,
                    "-Infinity" => double.NegativeInfinity,
                    var s       => throw new ArgumentException($"Invalid double value: {s}")
                }
                : element.GetDouble(),
            FieldType.Bytes   => ByteString.CopyFrom(Convert.FromBase64String(element.GetString() ?? "")),
            FieldType.Enum    => ConvertEnum(element, field),
            FieldType.Message => ConvertNestedMessage(element, field),
            _                 => null
        };
    }

    private static int ConvertEnum(JsonElement element, FieldDescriptor field)
    {
        var enumType = field.EnumType;

        switch (element.ValueKind)
        {
            // Handle string values (enum names)
            case JsonValueKind.String:

            {
                var enumName = element.GetString();

                if (string.IsNullOrEmpty(enumName))
                {
                    return 0; // Default enum value
                }

                // Try to find the enum value by name
                var enumValue = enumType.Values.FirstOrDefault(v => v.Name == enumName);

                return enumValue?.Number ?? throw new ArgumentException($"Unknown enum value '{enumName}' for enum type '{enumType.FullName}'");
            }

            // Handle numeric values (for backward compatibility)
            case JsonValueKind.Number:

                return element.GetInt32();

            case JsonValueKind.Undefined:
            case JsonValueKind.Object:
            case JsonValueKind.Array:
            case JsonValueKind.True:
            case JsonValueKind.False:
            case JsonValueKind.Null:
            default:

                throw new ArgumentException($"Invalid value for enum field '{field.Name}'. Expected string or number, got {element.ValueKind}");
        }
    }

    private SimpleDynamicMessage? ConvertNestedMessage(JsonElement element, FieldDescriptor field)
    {
        var messageType = field.MessageType;
        var fullName = messageType.FullName;

        // Handle well-known types with special JSON encoding
        switch (fullName)
        {
            case "google.protobuf.Timestamp":

                return WellKnownTypeHandler.ConvertTimestamp(element, messageType);

            case "google.protobuf.Duration":

                return WellKnownTypeHandler.ConvertDuration(element, messageType);

            case "google.protobuf.StringValue":
            case "google.protobuf.Int32Value":
            case "google.protobuf.Int64Value":
            case "google.protobuf.UInt32Value":
            case "google.protobuf.UInt64Value":
            case "google.protobuf.FloatValue":
            case "google.protobuf.DoubleValue":
            case "google.protobuf.BoolValue":
            case "google.protobuf.BytesValue":

                return WellKnownTypeHandler.ConvertWrapperType(element, messageType, ConvertJsonValue);

            case "google.protobuf.Any":

                return ConvertAnyElement(element, messageType);

            case "google.protobuf.Empty":

                return WellKnownTypeHandler.ConvertEmpty(messageType);

            case "google.protobuf.FieldMask":

                return WellKnownTypeHandler.ConvertFieldMask(element, messageType);

            case "google.protobuf.Struct":

                return WellKnownTypeHandler.ConvertStruct(element, messageType, ConvertValue);

            case "google.protobuf.Value":

                return WellKnownTypeHandler.ConvertValue(element, messageType, ConvertStruct, ConvertListValue);

            case "google.protobuf.ListValue":

                return WellKnownTypeHandler.ConvertListValue(element, messageType, ConvertValue);
        }

        // Regular message - must be JSON object
        if (element.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        // Recursively create a SimpleDynamicMessage for the nested message, honouring the
        // same unknown-field policy (maps/repeated/oneof handling included) as the top level.
        var nestedMessage = new SimpleDynamicMessage(messageType);
        nestedMessage.PopulateFromJsonObject(element, _allowUnknownFields);

        // Surface any unknown fields the nested message tracked with a dotted path so the
        // root request's UnknownFields warning reports `field.unknown`, not a bare `unknown`.
        foreach (var unknown in nestedMessage._unknownFields)
        {
            _unknownFields.Add($"{field.Name}.{unknown}");
        }

        return nestedMessage;
    }

    // Well-known types whose proto3 JSON form is not a plain object; inside an Any these
    // are wrapped as {"@type": ..., "value": <special-form>}.
    private static readonly HashSet<string> SpecialJsonWktNames = new(StringComparer.Ordinal)
    {
        "google.protobuf.Timestamp",
        "google.protobuf.Duration",
        "google.protobuf.FieldMask",
        "google.protobuf.Struct",
        "google.protobuf.Value",
        "google.protobuf.ListValue",
        "google.protobuf.DoubleValue",
        "google.protobuf.FloatValue",
        "google.protobuf.Int64Value",
        "google.protobuf.UInt64Value",
        "google.protobuf.Int32Value",
        "google.protobuf.UInt32Value",
        "google.protobuf.BoolValue",
        "google.protobuf.StringValue",
        "google.protobuf.BytesValue"
    };

    /// <summary>
    ///     Converts a proto3-JSON <c>google.protobuf.Any</c> object into an Any message whose
    ///     <c>value</c> field holds the binary protobuf of the embedded message (per spec), rather
    ///     than treating <c>value</c> as opaque JSON text. The embedded type is resolved by
    ///     <c>@type</c> against the descriptor closure of the message being processed.
    /// </summary>
    private SimpleDynamicMessage? ConvertAnyElement(JsonElement element, MessageDescriptor anyDescriptor)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var message = new SimpleDynamicMessage(anyDescriptor);
        var typeUrlField = anyDescriptor.FindFieldByNumber(1);
        var valueField = anyDescriptor.FindFieldByNumber(2);

        if (!element.TryGetProperty("@type", out var typeUrlElement) || typeUrlElement.ValueKind != JsonValueKind.String)
        {
            // No @type: not a populated Any. Leave it empty rather than guessing.
            return message;
        }

        var typeUrl = typeUrlElement.GetString()!;

        if (typeUrlField is not null)
        {
            message.Fields[typeUrlField] = typeUrl;
        }

        if (valueField is null)
        {
            return message;
        }

        var embeddedDescriptor = AnyTypeResolver.ForContext(Descriptor).Resolve(typeUrl)
                                 ?? throw new ArgumentException(
                                     $"Cannot resolve google.protobuf.Any type '{typeUrl}'. The type must be present " +
                                     "in the loaded descriptors (reflection, protoset, or proto files).");

        var embedded = SpecialJsonWktNames.Contains(embeddedDescriptor.FullName)
            ? BuildSpecialWktFromAny(element, embeddedDescriptor)
            : BuildRegularMessageFromAny(element, embeddedDescriptor);

        message.Fields[valueField] = embedded is null ? ByteString.Empty : embedded.ToByteString();

        return message;
    }

    private SimpleDynamicMessage? BuildRegularMessageFromAny(JsonElement element, MessageDescriptor embeddedDescriptor)
    {
        // Regular messages inline their fields alongside @type; rebuild the embedded JSON
        // object without @type and parse it through the standard path (maps/repeated/oneofs).
        using var buffer = new MemoryStream();

        using (var jsonWriter = new Utf8JsonWriter(buffer))
        {
            jsonWriter.WriteStartObject();

            foreach (var property in element.EnumerateObject().Where(p => p.Name != "@type"))
            {
                property.WriteTo(jsonWriter);
            }

            jsonWriter.WriteEndObject();
        }

        var embeddedJson = Encoding.UTF8.GetString(buffer.ToArray());

        // Honour the in-force unknown-field policy: strict mode must reject unknown fields
        // inside an Any payload too, rather than silently dropping them.
        return new SimpleDynamicMessage(embeddedDescriptor, embeddedJson, _allowUnknownFields);
    }

    private SimpleDynamicMessage? BuildSpecialWktFromAny(JsonElement element, MessageDescriptor embeddedDescriptor)
    {
        // Special well-known types appear as {"@type": ..., "value": <special-form>}.
        if (!element.TryGetProperty("value", out var valueElement))
        {
            return new SimpleDynamicMessage(embeddedDescriptor);
        }

        return embeddedDescriptor.FullName switch
        {
            "google.protobuf.Timestamp"   => WellKnownTypeHandler.ConvertTimestamp(valueElement, embeddedDescriptor),
            "google.protobuf.Duration"    => WellKnownTypeHandler.ConvertDuration(valueElement, embeddedDescriptor),
            "google.protobuf.FieldMask"   => WellKnownTypeHandler.ConvertFieldMask(valueElement, embeddedDescriptor),
            "google.protobuf.Struct"      => WellKnownTypeHandler.ConvertStruct(valueElement, embeddedDescriptor, ConvertValue),
            "google.protobuf.Value"       => WellKnownTypeHandler.ConvertValue(valueElement, embeddedDescriptor, ConvertStruct, ConvertListValue),
            "google.protobuf.ListValue"   => WellKnownTypeHandler.ConvertListValue(valueElement, embeddedDescriptor, ConvertValue),
            _                             => WellKnownTypeHandler.ConvertWrapperType(valueElement, embeddedDescriptor, ConvertJsonValue)
        };
    }

    /// <summary>
    ///     Renders a <c>google.protobuf.Any</c> message to proto3 JSON, decoding the binary
    ///     <c>value</c> payload by resolving <c>@type</c>. Unresolvable types fall back to a
    ///     base64 rendering (upstream-grpcurl style) so no information is lost.
    /// </summary>
    private void WriteAnyJson(StringBuilder sb, SimpleDynamicMessage any, bool includeDefaults)
    {
        var typeUrlField = any.Descriptor.FindFieldByNumber(1);
        var valueField = any.Descriptor.FindFieldByNumber(2);

        var typeUrl = typeUrlField is not null && any.Fields.TryGetValue(typeUrlField, out var t) ? t as string : null;
        var valueBytes = valueField is not null && any.Fields.TryGetValue(valueField, out var v) && v is ByteString bs
            ? bs
            : ByteString.Empty;

        sb.Append('{');

        if (string.IsNullOrEmpty(typeUrl))
        {
            sb.Append('}');

            return;
        }

        sb.Append("\"@type\":\"").Append(JsonEncodedText.Encode(typeUrl)).Append('"');

        var embeddedDescriptor = AnyTypeResolver.ForContext(Descriptor).Resolve(typeUrl);

        if (embeddedDescriptor is null)
        {
            // Unresolvable: preserve the payload verbatim as base64 with an explicit marker.
            sb.Append(",\"@error\":\"type not found\",\"value\":\"")
              .Append(Convert.ToBase64String(valueBytes.ToByteArray()))
              .Append('"');
            sb.Append('}');

            return;
        }

        var embedded = new SimpleDynamicMessage(embeddedDescriptor);

        using (var input = new CodedInputStream(valueBytes.ToByteArray()))
        {
            embedded.MergeFrom(input);
        }

        if (SpecialJsonWktNames.Contains(embeddedDescriptor.FullName))
        {
            sb.Append(",\"value\":");
            WriteSpecialWktInline(sb, embedded, embeddedDescriptor, includeDefaults);
        }
        else
        {
            var inner = embedded.ToJson(includeDefaults).Trim();

            // Inline the embedded fields after @type; ToJson always returns a JSON object.
            if (inner.Length > 2 && inner[0] == '{' && inner[^1] == '}')
            {
                sb.Append(',').Append(inner[1..^1]);
            }
        }

        sb.Append('}');
    }

    private void WriteSpecialWktInline(StringBuilder sb, SimpleDynamicMessage embedded, MessageDescriptor descriptor, bool includeDefaults)
    {
        switch (descriptor.FullName)
        {
            case "google.protobuf.Timestamp":

                WellKnownTypeHandler.WriteTimestampJson(sb, embedded);

                break;

            case "google.protobuf.Duration":

                WellKnownTypeHandler.WriteDurationJson(sb, embedded);

                break;

            case "google.protobuf.FieldMask":

                WellKnownTypeHandler.WriteFieldMaskJson(sb, embedded);

                break;

            case "google.protobuf.Struct":

                WellKnownTypeHandler.WriteStructJson(sb, embedded, WriteValueJson);

                break;

            case "google.protobuf.Value":

                WellKnownTypeHandler.WriteValueJson(sb, embedded, WriteStructJson, WriteListValueJson);

                break;

            case "google.protobuf.ListValue":

                WellKnownTypeHandler.WriteListValueJson(sb, embedded, WriteValueJson);

                break;

            default:

                WellKnownTypeHandler.WriteWrapperJson(sb, embedded, descriptor,
                                                      (s, f, val) => WriteJsonValue(s, f, val, includeDefaults));

                break;
        }
    }

    // Adapter methods for WellKnownTypeHandler callbacks
    private SimpleDynamicMessage ConvertValue(JsonElement element, MessageDescriptor messageType)
        => WellKnownTypeHandler.ConvertValue(element, messageType, ConvertStruct, ConvertListValue);

    private SimpleDynamicMessage? ConvertStruct(JsonElement element, MessageDescriptor messageType)
        => WellKnownTypeHandler.ConvertStruct(element, messageType, ConvertValue);

    private SimpleDynamicMessage? ConvertListValue(JsonElement element, MessageDescriptor messageType)
        => WellKnownTypeHandler.ConvertListValue(element, messageType, ConvertValue);

    private static object ConvertMapKey(string keyString, FieldDescriptor keyField)
    {
        // Map keys are always strings in JSON, but need to be converted to the correct type
        return keyField.FieldType switch
        {
            FieldType.String                                          => keyString,
            FieldType.Int32 or FieldType.SInt32 or FieldType.SFixed32 => int.Parse(keyString),
            FieldType.Int64 or FieldType.SInt64 or FieldType.SFixed64 => long.Parse(keyString),
            FieldType.UInt32 or FieldType.Fixed32                     => uint.Parse(keyString),
            FieldType.UInt64 or FieldType.Fixed64                     => ulong.Parse(keyString),
            FieldType.Bool                                            => bool.Parse(keyString),
            _                                                         => keyString
        };
    }

    /// <summary>
    ///     Returns the default value for a scalar field type.
    ///     Protobuf scalars don't have null semantics - they default to zero/empty values.
    /// </summary>
    private static object GetDefaultValue(FieldDescriptor field)
        => field.FieldType switch
        {
            FieldType.String                                          => "",
            FieldType.Int32 or FieldType.SInt32 or FieldType.SFixed32 => 0,
            FieldType.Int64 or FieldType.SInt64 or FieldType.SFixed64 => 0L,
            FieldType.UInt32 or FieldType.Fixed32                     => 0u,
            FieldType.UInt64 or FieldType.Fixed64                     => 0UL,
            FieldType.Bool                                            => false,
            FieldType.Float                                           => 0f,
            FieldType.Double                                          => 0d,
            FieldType.Bytes                                           => ByteString.Empty,
            FieldType.Enum                                            => 0,
            _                                                         => throw new ArgumentException($"No default value for field type: {field.FieldType}")
        };

    /// <summary>
    ///     Converts this dynamic message to JSON string.
    /// </summary>
    /// <param name="includeDefaults">
    ///     When true, includes fields with default/null values in output.
    ///     When false (default), omits fields with null or default values.
    /// </param>
    /// <returns>JSON representation of the message.</returns>
    /// <remarks>
    ///     <para>
    ///         Proto3 semantics: In proto3, there is no distinction between a field that was never
    ///         set and a field explicitly set to its default value. Both null message fields and
    ///         unset fields are omitted from the JSON output when includeDefaults is false.
    ///     </para>
    ///     <para>
    ///         This matches the canonical proto3 JSON encoding behavior where default values are
    ///         not emitted. Use includeDefaults=true (--emit-defaults CLI flag) to see all fields.
    ///     </para>
    /// </remarks>
    public string ToJson(bool includeDefaults = false)
    {
        var sb = new StringBuilder().Append('{');

        var first = true;

        // Write non-repeated fields
        foreach (var (field, value) in Fields)
        {
            // Skip null values unless includeDefaults is true
            // Note: Proto3 does not distinguish between "unset" and "default value" for scalars
            if (value is null && !includeDefaults)
            {
                continue;
            }

            if (!first)
            {
                sb.Append(',');
            }

            first = false;

            // Write field name (use proto field name for snake_case, matching Go grpcurl)
            sb.Append('"');
            sb.Append(field.Name);
            sb.Append("\":");

            // Write field value
            WriteJsonValue(sb, field, value, includeDefaults);
        }

        // Write repeated fields as arrays
        foreach (var (field, values) in RepeatedFields)
        {
            if (values.Count == 0 && !includeDefaults)
            {
                continue;
            }

            if (!first)
            {
                sb.Append(',');
            }

            first = false;

            // Write field name (use proto field name for snake_case, matching Go grpcurl)
            sb.Append('"');
            sb.Append(field.Name);
            sb.Append("\":[");

            // Write array elements
            for (var i = 0; i < values.Count; i++)
            {
                if (i > 0)
                {
                    sb.Append(',');
                }

                WriteJsonValue(sb, field, values[i], includeDefaults);
            }

            sb.Append(']');
        }

        // Write map fields as JSON objects
        foreach (var (field, map) in MapFields)
        {
            if (map.Count == 0 && !includeDefaults)
            {
                continue;
            }

            if (!first)
            {
                sb.Append(',');
            }

            first = false;

            // Write field name (use proto field name for snake_case, matching Go grpcurl)
            sb.Append('"');
            sb.Append(field.Name);
            sb.Append("\":{");

            // Get value field descriptor (key field not needed as FormatMapKey handles all valid key types)
            var mapDescriptor = field.MessageType;
            var valueField = mapDescriptor.FindFieldByNumber(2);

            var firstEntry = true;

            foreach (var (key, value) in map)
            {
                if (!firstEntry)
                {
                    sb.Append(',');
                }

                firstEntry = false;

                // Write key as string (JSON object keys are always strings)
                sb.Append('"');
                sb.Append(FormatMapKey(key));
                sb.Append("\":");

                // Write value
                if (valueField is not null)
                {
                    WriteJsonValue(sb, valueField, value, includeDefaults);
                }
            }

            sb.Append('}');
        }

        // Emit default values for fields not already written
        if (includeDefaults)
        {
            foreach (var field in Descriptor.Fields.InDeclarationOrder())
            {
                // Skip if already written from populated dictionaries
                if (Fields.ContainsKey(field) || RepeatedFields.ContainsKey(field) || MapFields.ContainsKey(field))
                {
                    continue;
                }

                if (!first)
                {
                    sb.Append(',');
                }

                first = false;

                sb.Append('"');
                sb.Append(field.Name);
                sb.Append("\":");

                if (field.IsMap)
                {
                    sb.Append("{}");
                }
                else if (field.IsRepeated)
                {
                    sb.Append("[]");
                }
                else
                {
                    WriteDefaultValue(sb, field);
                }
            }
        }

        sb.Append('}');

        return sb.ToString();
    }

    private static void WriteDefaultValue(StringBuilder sb, FieldDescriptor field)
    {
        switch (field.FieldType)
        {
            case FieldType.String:

                sb.Append("\"\"");

                break;

            case FieldType.Bool:

                sb.Append("false");

                break;

            case FieldType.Int32:
            case FieldType.SInt32:
            case FieldType.SFixed32:
            case FieldType.UInt32:
            case FieldType.Fixed32:
            case FieldType.Float:
            case FieldType.Double:

                sb.Append('0');

                break;

            case FieldType.Int64:
            case FieldType.SInt64:
            case FieldType.SFixed64:

            case FieldType.UInt64:
            case FieldType.Fixed64:

                sb.Append("\"0\"");

                break;

            case FieldType.Bytes:

                sb.Append("\"\"");

                break;

            case FieldType.Enum:

                var enumValue = field.EnumType.Values.FirstOrDefault();

                if (enumValue is not null)
                {
                    sb.Append('"');
                    sb.Append(enumValue.Name);
                    sb.Append('"');
                }
                else
                {
                    sb.Append('0');
                }

                break;

            // ReSharper disable once DuplicatedSwitchSectionBodies for clarity
            case FieldType.Message:
            case FieldType.Group:

                sb.Append("null");

                break;

            default:

                sb.Append("null");

                break;
        }
    }

    private static string FormatMapKey(object key) // Convert the key to a string for JSON
        => key.ToString() ?? "";

    // proto3 JSON represents non-finite floating-point values as the quoted string
    // tokens "NaN", "Infinity", and "-Infinity"; bare tokens are not valid JSON and
    // make a successful RPC's response unparseable downstream (DynamicInvoker.MessageToJson).
    private static void AppendFloatingPoint(StringBuilder sb, float value)
    {
        if (TryAppendNonFinite(sb, float.IsNaN(value), float.IsPositiveInfinity(value), float.IsNegativeInfinity(value)))
        {
            return;
        }

        sb.Append(value.ToString("G", CultureInfo.InvariantCulture));
    }

    private static void AppendFloatingPoint(StringBuilder sb, double value)
    {
        if (TryAppendNonFinite(sb, double.IsNaN(value), double.IsPositiveInfinity(value), double.IsNegativeInfinity(value)))
        {
            return;
        }

        sb.Append(value.ToString("G", CultureInfo.InvariantCulture));
    }

    private static bool TryAppendNonFinite(StringBuilder sb, bool isNaN, bool isPositiveInfinity, bool isNegativeInfinity)
    {
        if (isNaN)
        {
            sb.Append("\"NaN\"");
        }
        else if (isPositiveInfinity)
        {
            sb.Append("\"Infinity\"");
        }
        else if (isNegativeInfinity)
        {
            sb.Append("\"-Infinity\"");
        }
        else
        {
            return false;
        }

        return true;
    }

    private void WriteJsonValue(StringBuilder sb, FieldDescriptor field, object? value, bool includeDefaults = false)
    {
        if (value is null)
        {
            sb.Append("null");

            return;
        }

        switch (field.FieldType)
        {
            case FieldType.String:

                sb.Append('"');
                sb.Append(JsonEncodedText.Encode((string)value).ToString());
                sb.Append('"');

                break;

            case FieldType.Int32:
            case FieldType.SInt32:
            case FieldType.SFixed32:

                sb.Append((int)value);

                break;

            case FieldType.Int64:
            case FieldType.SInt64:
            case FieldType.SFixed64:

                sb.Append('"');
                sb.Append((long)value);
                sb.Append('"'); // JSON uses strings for int64

                break;

            case FieldType.UInt32:
            case FieldType.Fixed32:

                sb.Append((uint)value);

                break;

            case FieldType.UInt64:
            case FieldType.Fixed64:

                sb.Append('"');
                sb.Append((ulong)value);
                sb.Append('"'); // JSON uses strings for uint64

                break;

            case FieldType.Bool:

                sb.Append((bool)value ? "true" : "false");

                break;

            case FieldType.Float:

                AppendFloatingPoint(sb, (float)value);

                break;

            case FieldType.Double:

                AppendFloatingPoint(sb, (double)value);

                break;

            case FieldType.Bytes:

                sb.Append('"');
                sb.Append(Convert.ToBase64String(((ByteString)value).ToByteArray()));
                sb.Append('"');

                break;

            case FieldType.Enum:

            {
                var enumValue = field.EnumType.Values.FirstOrDefault(v => v.Number == (int)value);

                if (enumValue is not null)
                {
                    sb.Append('"');
                    sb.Append(enumValue.Name);
                    sb.Append('"');
                }
                else
                {
                    // Fallback to integer if enum value not found
                    sb.Append((int)value);
                }

                break;
            }

            case FieldType.Message:

                // Recursively serialize nested message
                if (value is SimpleDynamicMessage nestedMessage)
                {
                    // Check for well-known types with special JSON encoding
                    var fullName = field.MessageType.FullName;

                    switch (fullName)
                    {
                        case "google.protobuf.Timestamp":

                            WellKnownTypeHandler.WriteTimestampJson(sb, nestedMessage);

                            break;

                        case "google.protobuf.Duration":

                            WellKnownTypeHandler.WriteDurationJson(sb, nestedMessage);

                            break;

                        case "google.protobuf.StringValue":
                        case "google.protobuf.Int32Value":
                        case "google.protobuf.Int64Value":
                        case "google.protobuf.UInt32Value":
                        case "google.protobuf.UInt64Value":
                        case "google.protobuf.FloatValue":
                        case "google.protobuf.DoubleValue":
                        case "google.protobuf.BoolValue":
                        case "google.protobuf.BytesValue":

                            WellKnownTypeHandler.WriteWrapperJson(sb, nestedMessage, field.MessageType,
                                                                  (s, f, v) => WriteJsonValue(s, f, v, includeDefaults));

                            break;

                        case "google.protobuf.Any":

                            WriteAnyJson(sb, nestedMessage, includeDefaults);

                            break;

                        case "google.protobuf.Empty":

                            WellKnownTypeHandler.WriteEmptyJson(sb);

                            break;

                        case "google.protobuf.FieldMask":

                            WellKnownTypeHandler.WriteFieldMaskJson(sb, nestedMessage);

                            break;

                        case "google.protobuf.Struct":

                            WellKnownTypeHandler.WriteStructJson(sb, nestedMessage, WriteValueJson);

                            break;

                        case "google.protobuf.Value":

                            WellKnownTypeHandler.WriteValueJson(sb, nestedMessage, WriteStructJson, WriteListValueJson);

                            break;

                        case "google.protobuf.ListValue":

                            WellKnownTypeHandler.WriteListValueJson(sb, nestedMessage, WriteValueJson);

                            break;

                        default:

                            // Regular message — pass includeDefaults for recursive emit-defaults
                            sb.Append(nestedMessage.ToJson(includeDefaults));

                            break;
                    }
                }
                else
                {
                    sb.Append("null");
                }

                break;

            case FieldType.Group:

                // Groups are a deprecated proto2 feature not supported in proto3.
                // Modern gRPC services use proto3, so Group support is not implemented.
                sb.Append("null");

                break;

            default:

                sb.Append("null");

                break;
        }
    }

    // Adapter methods for WellKnownTypeHandler JSON serialization callbacks
    private void WriteStructJson(StringBuilder sb, SimpleDynamicMessage structMsg)
        => WellKnownTypeHandler.WriteStructJson(sb, structMsg, WriteValueJson);

    private void WriteValueJson(StringBuilder sb, SimpleDynamicMessage value)
        => WellKnownTypeHandler.WriteValueJson(sb, value, WriteStructJson, WriteListValueJson);

    private void WriteListValueJson(StringBuilder sb, SimpleDynamicMessage listValue)
        => WellKnownTypeHandler.WriteListValueJson(sb, listValue, WriteValueJson);
}