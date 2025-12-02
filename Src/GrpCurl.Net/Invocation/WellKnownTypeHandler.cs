using Google.Protobuf;
using Google.Protobuf.Reflection;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace GrpCurl.Net.Invocation;

/// <summary>
///     Handles conversion of Google protobuf well-known types between JSON and protobuf formats.
/// </summary>
internal static class WellKnownTypeHandler
{
    /// <summary>
    ///     Provides JSON Deserialization from JSON to Protobuf.
    /// </summary>
    /// <param name="element">The JSON element.</param>
    /// <param name="messageType">The message descriptor.</param>
    /// <returns>An instance of <see cref="SimpleDynamicMessage"/> representing the protobuf format, or null if the provided JSON element is not string.</returns>
    public static SimpleDynamicMessage? ConvertTimestamp(JsonElement element, MessageDescriptor messageType)
    {
        // Timestamp is encoded as RFC 3339 string in JSON
        if (element.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        var timestampStr = element.GetString();

        if (string.IsNullOrEmpty(timestampStr))
        {
            return null;
        }

        // Parse RFC 3339 timestamp
        if (!DateTime.TryParse(timestampStr, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var dateTime))
        {
            return null;
        }

        // Ensure UTC
        if (dateTime.Kind != DateTimeKind.Utc)
        {
            dateTime = dateTime.ToUniversalTime();
        }

        // Convert to protobuf Timestamp format (seconds and nanos since epoch)
        var epoch = DateTime.UnixEpoch;
        var duration = dateTime - epoch;
        var seconds = (long)duration.TotalSeconds;
        var nanos = (int)((duration.TotalSeconds - seconds) * 1_000_000_000);

        var message = new SimpleDynamicMessage(messageType);
        var secondsField = messageType.FindFieldByNumber(1);
        var nanosField = messageType.FindFieldByNumber(2);

        if (secondsField is not null)
        {
            message.Fields[secondsField] = seconds;
        }

        if (nanosField is not null)
        {
            message.Fields[nanosField] = nanos;
        }

        return message;
    }

    public static SimpleDynamicMessage? ConvertDuration(JsonElement element, MessageDescriptor messageType)
    {
        // Duration is encoded as string like "1.000340012s" in JSON
        if (element.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        var durationStr = element.GetString();

        if (string.IsNullOrEmpty(durationStr) || !durationStr.EndsWith('s'))
        {
            return null;
        }

        // Remove the 's' suffix
        var numberStr = durationStr[..^1];

        // Parse seconds and fractional nanoseconds separately to avoid precision loss
        var parts = numberStr.Split('.');

        if (!long.TryParse(parts[0], out var seconds))
        {
            return null;
        }

        var nanos = 0;

        if (parts.Length > 1)
        {
            // Pad fractional part to 9 digits (nanoseconds) and parse
            var fractional = parts[1].PadRight(9, '0');

            if (fractional.Length > 9)
            {
                fractional = fractional[..9]; // Take only first 9 digits
            }

            if (!int.TryParse(fractional, out nanos))
            {
                return null;
            }
        }

        var message = new SimpleDynamicMessage(messageType);
        var secondsField = messageType.FindFieldByNumber(1);
        var nanosField = messageType.FindFieldByNumber(2);

        if (secondsField is not null)
        {
            message.Fields[secondsField] = seconds;
        }

        if (nanosField is not null)
        {
            message.Fields[nanosField] = nanos;
        }

        return message;
    }

    public static SimpleDynamicMessage? ConvertWrapperType(JsonElement element, MessageDescriptor messageType, Func<JsonElement, FieldDescriptor, object?> convertJsonValue)
    {
        // Wrapper types are encoded as the raw value in JSON, not as an object
        var valueField = messageType.FindFieldByNumber(1); // All wrappers have "value" as field 1

        if (valueField is null)
        {
            return null;
        }

        var value = convertJsonValue(element, valueField);
        var message = new SimpleDynamicMessage(messageType)
        {
            Fields =
            {
                [valueField] = value
            }
        };

        return message;
    }

    public static SimpleDynamicMessage? ConvertAny(JsonElement element, MessageDescriptor messageType)
    {
        // Any is encoded as JSON object with "@type" field and embedded message
        if (element.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var message = new SimpleDynamicMessage(messageType);

        // Field 1: type_url (string)
        var typeUrlField = messageType.FindFieldByNumber(1);

        // Field 2: value (bytes) - serialized embedded message
        var valueField = messageType.FindFieldByNumber(2);

        if (typeUrlField is not null && element.TryGetProperty("@type", out var typeUrlElement))
        {
            message.Fields[typeUrlField] = typeUrlElement.GetString();
        }

        // For simplicity, we'll serialize the entire JSON (excluding @type) as bytes
        // A full implementation would parse the embedded message based on type_url
        if (valueField is null)
        {
            return message;
        }

        // Create a JSON object without the @type field
        var valueJson = new StringBuilder();

        valueJson.Append('{');

        var first = true;

        foreach (var property in element.EnumerateObject().Where(property => property.Name != "@type"))
        {
            if (!first)
            {
                valueJson.Append(',');
            }

            first = false;

            valueJson.Append('"');
            valueJson.Append(property.Name);
            valueJson.Append("\":");
            valueJson.Append(property.Value.GetRawText());
        }

        valueJson.Append('}');

        // Encode as UTF-8 bytes
        var bytes = Encoding.UTF8.GetBytes(valueJson.ToString());

        message.Fields[valueField] = ByteString.CopyFrom(bytes);

        return message;
    }

    /// <summary>
    ///     Empty is encoded as empty JSON object. Just return an empty message.
    /// </summary>
    /// <param name="messageType">The message type.</param>
    /// <returns>A <see cref="SimpleDynamicMessage"/> representing the empty message.</returns>
    public static SimpleDynamicMessage ConvertEmpty(MessageDescriptor messageType)
        => new(messageType);

    public static SimpleDynamicMessage? ConvertFieldMask(JsonElement element, MessageDescriptor messageType)
    {
        // FieldMask is encoded as a single string with comma-separated paths
        if (element.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        var pathsString = element.GetString();

        if (string.IsNullOrEmpty(pathsString))
        {
            return new SimpleDynamicMessage(messageType);
        }

        var message = new SimpleDynamicMessage(messageType);

        // Field 1: paths (repeated string)
        var pathsField = messageType.FindFieldByNumber(1);

        if (pathsField is null)
        {
            return message;
        }

        // Split by comma and trim whitespace
        var paths = pathsString.Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(p => p.Trim())
            .Cast<object?>()
            .ToList();

        message.RepeatedFields[pathsField] = paths;

        return message;
    }

    public static SimpleDynamicMessage? ConvertStruct(JsonElement element, MessageDescriptor messageType, Func<JsonElement, MessageDescriptor, SimpleDynamicMessage?> convertValue)
    {
        // Struct is encoded as a JSON object
        if (element.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var message = new SimpleDynamicMessage(messageType);

        // Field 1: fields (map<string, Value>)
        var fieldsField = messageType.FindFieldByNumber(1);

        if (fieldsField is not { IsMap: true })
        {
            return message;
        }

        message.MapFields[fieldsField] = [];

        var mapDescriptor = fieldsField.MessageType;
        var valueField = mapDescriptor.FindFieldByNumber(2); // Value field in map entry

        if (valueField is null)
        {
            return message;
        }

        foreach (var property in element.EnumerateObject())
        {
            // Convert each property value to a google.protobuf.Value message
            var valueMessage = convertValue(property.Value, valueField.MessageType);

            message.MapFields[fieldsField][property.Name] = valueMessage;
        }

        return message;
    }

    public static SimpleDynamicMessage ConvertValue(JsonElement element, MessageDescriptor messageType, Func<JsonElement, MessageDescriptor, SimpleDynamicMessage?> convertStruct, Func<JsonElement, MessageDescriptor, SimpleDynamicMessage?> convertListValue)
    {
        // Value is encoded as the raw JSON value
        var message = new SimpleDynamicMessage(messageType);

        /*
         * google.protobuf.Value has oneof kind with fields:
         * 
         * 1: null_value (NullValue enum)
         * 2: number_value (double)
         * 3: string_value (string)
         * 4: bool_value (bool)
         * 5: struct_value (Struct)
         * 6: list_value (ListValue)
        */
        FieldDescriptor? activeField;
        object? value;

        switch (element.ValueKind)
        {
            case JsonValueKind.Null:

                activeField = messageType.FindFieldByNumber(1); // null_value
                value = 0; // NullValue.NULL_VALUE = 0

                break;

            case JsonValueKind.Number:

                activeField = messageType.FindFieldByNumber(2); // number_value
                value = element.GetDouble();

                break;

            case JsonValueKind.String:

                activeField = messageType.FindFieldByNumber(3); // string_value
                value = element.GetString();

                break;

            case JsonValueKind.True:
            case JsonValueKind.False:

                activeField = messageType.FindFieldByNumber(4); // bool_value
                value = element.GetBoolean();

                break;

            case JsonValueKind.Object:

                activeField = messageType.FindFieldByNumber(5); // struct_value
                value = convertStruct(element, activeField.MessageType);

                break;

            case JsonValueKind.Array:

                activeField = messageType.FindFieldByNumber(6); // list_value
                value = convertListValue(element, activeField.MessageType);

                break;

            case JsonValueKind.Undefined:
            default:
                
                throw new InvalidOperationException("Invalid JSON Value Kind.");
        }

        if (activeField is null || value is null)
        {
            return message;
        }

        message.Fields[activeField] = value;

        // Track oneof
        if (activeField.ContainingOneof is not null)
        {
            message.OneofFields[activeField.ContainingOneof] = activeField;
        }

        return message;
    }

    public static SimpleDynamicMessage? ConvertListValue(JsonElement element, MessageDescriptor messageType, Func<JsonElement, MessageDescriptor, SimpleDynamicMessage?> convertValue)
    {
        // ListValue is encoded as a JSON array
        if (element.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var message = new SimpleDynamicMessage(messageType);

        // Field 1: values (repeated Value)
        var valuesField = messageType.FindFieldByNumber(1);

        if (valuesField is null)
        {
            return message;
        }

        message.RepeatedFields[valuesField] = [];

        foreach (var valueMessage in element.EnumerateArray().Select(item => convertValue(item, valuesField.MessageType)))
        {
            message.RepeatedFields[valuesField].Add(valueMessage);
        }

        return message;
    }

    /// <summary>
    ///     Provides serialisation from Protobuf to JSON.
    /// </summary>
    /// <param name="sb">The string builder.</param>
    /// <param name="timestamp">A <see cref="SimpleDynamicMessage"/> reprsenting the timestamp to write.</param>
    public static void WriteTimestampJson(StringBuilder sb, SimpleDynamicMessage timestamp)
    {
        // Get seconds and nanos fields
        var secondsField = timestamp.Descriptor.FindFieldByNumber(1);
        var nanosField = timestamp.Descriptor.FindFieldByNumber(2);

        var seconds = secondsField is not null && timestamp.Fields.TryGetValue(secondsField, out var field)
            ? (long)field!
            : 0L;

        var nanos = nanosField is not null && timestamp.Fields.TryGetValue(nanosField, out var timestampField)
            ? (int)timestampField!
            : 0;

        // Convert to DateTime
        var epoch = DateTime.UnixEpoch;
        var dateTime = epoch.AddSeconds(seconds).AddTicks(nanos / 100);

        // Format as RFC 3339
        sb.Append('"');
        sb.Append(dateTime.ToString("yyyy-MM-ddTHH:mm:ss"));

        if (nanos % 1_000_000_000 != 0)
        {
            sb.Append('.');
            sb.Append((nanos % 1_000_000_000).ToString("D9").TrimEnd('0'));
        }

        sb.Append('Z');
        sb.Append('"');
    }

    public static void WriteDurationJson(StringBuilder sb, SimpleDynamicMessage duration)
    {
        // Get seconds and nanos fields
        var secondsField = duration.Descriptor.FindFieldByNumber(1);
        var nanosField = duration.Descriptor.FindFieldByNumber(2);

        var seconds = secondsField is not null && duration.Fields.TryGetValue(secondsField, out var field)
            ? (long)field!
            : 0L;

        var nanos = nanosField is not null && duration.Fields.TryGetValue(nanosField, out var durationField)
            ? (int)durationField!
            : 0;

        // Format as string like "1.000340012s"
        sb.Append('"');
        sb.Append(seconds);

        if (nanos != 0)
        {
            sb.Append('.');
            sb.Append(nanos.ToString("D9").TrimEnd('0'));
        }

        sb.Append('s');
        sb.Append('"');
    }

    public static void WriteWrapperJson(StringBuilder sb, SimpleDynamicMessage wrapper, MessageDescriptor messageType, Action<StringBuilder, FieldDescriptor, object?> writeJsonValue)
    {
        // Get the value field (field 1)
        var valueField = messageType.FindFieldByNumber(1);

        if (valueField is not null && wrapper.Fields.TryGetValue(valueField, out var value))
        {
            writeJsonValue(sb, valueField, value);
        }
        else
        {
            sb.Append("null");
        }
    }

    public static void WriteAnyJson(StringBuilder sb, SimpleDynamicMessage any, MessageDescriptor messageType)
    {
        // Any is encoded as JSON object with "@type" field and embedded message fields
        var typeUrlField = messageType.FindFieldByNumber(1);
        var valueField = messageType.FindFieldByNumber(2);

        sb.Append('{');

        // Write @type field
        if (typeUrlField is not null && any.Fields.TryGetValue(typeUrlField, out var typeUrl))
        {
            sb.Append("\"@type\":\"");
            sb.Append(JsonEncodedText.Encode((string)typeUrl!).ToString());
            sb.Append('"');
        }

        // Write embedded message fields
        if (valueField is not null && any.Fields.TryGetValue(valueField, out var valueBytes) && valueBytes is ByteString bytes)
        {
            // Parse the bytes as JSON and merge into the output
            var jsonString = Encoding.UTF8.GetString(bytes.ToByteArray());

            if (!string.IsNullOrEmpty(jsonString) && jsonString != "{}")
            {
                sb.Append(',');

                // Remove outer braces and append the inner content
                var innerJson = jsonString.Trim();

                if (innerJson.StartsWith('{') && innerJson.EndsWith('}'))
                {
                    innerJson = innerJson[1..^1];
                }

                sb.Append(innerJson);
            }
        }

        sb.Append('}');
    }

    public static void WriteEmptyJson(StringBuilder sb)
    {
        // Empty is encoded as empty JSON object
        sb.Append("{}");
    }

    public static void WriteFieldMaskJson(StringBuilder sb, SimpleDynamicMessage fieldMask)
    {
        // FieldMask is encoded as a single string with comma-separated paths
        var pathsField = fieldMask.Descriptor.FindFieldByNumber(1);

        sb.Append('"');

        if (pathsField is not null && fieldMask.RepeatedFields.TryGetValue(pathsField, out var paths) && paths.Count > 0)
        {
            for (var i = 0; i < paths.Count; i++)
            {
                if (i > 0)
                {
                    sb.Append(',');
                }

                sb.Append(paths[i]);
            }
        }

        sb.Append('"');
    }

    public static void WriteStructJson(StringBuilder sb, SimpleDynamicMessage structMsg, Action<StringBuilder, SimpleDynamicMessage> writeValueJson)
    {
        // Struct is encoded as a JSON object
        var fieldsField = structMsg.Descriptor.FindFieldByNumber(1);

        sb.Append('{');

        if (fieldsField is not null && structMsg.MapFields.TryGetValue(fieldsField, out var fields))
        {
            var first = true;

            foreach (var (key, value) in fields)
            {
                if (!first)
                {
                    sb.Append(',');
                }

                first = false;

                sb.Append('"');
                sb.Append(key);
                sb.Append("\":");

                if (value is SimpleDynamicMessage valueMsg)
                {
                    writeValueJson(sb, valueMsg);
                }
                else
                {
                    sb.Append("null");
                }
            }
        }

        sb.Append('}');
    }

    public static void WriteValueJson(StringBuilder sb, SimpleDynamicMessage value, Action<StringBuilder, SimpleDynamicMessage> writeStructJson, Action<StringBuilder, SimpleDynamicMessage> writeListValueJson)
    {
        // Value is encoded as the raw JSON value
        var nullField = value.Descriptor.FindFieldByNumber(1);
        var numberField = value.Descriptor.FindFieldByNumber(2);
        var stringField = value.Descriptor.FindFieldByNumber(3);
        var boolField = value.Descriptor.FindFieldByNumber(4);
        var structField = value.Descriptor.FindFieldByNumber(5);
        var listField = value.Descriptor.FindFieldByNumber(6);

        // Check which field is set in the oneof
        if (nullField is not null && value.Fields.ContainsKey(nullField))
        {
            sb.Append("null");
        }
        else if (numberField is not null && value.Fields.TryGetValue(numberField, out var numberValue))
        {
            sb.Append(((double)numberValue!).ToString("G", CultureInfo.InvariantCulture));
        }
        else if (stringField is not null && value.Fields.TryGetValue(stringField, out var stringValue))
        {
            sb.Append('"');
            sb.Append(JsonEncodedText.Encode((string)stringValue!).ToString());
            sb.Append('"');
        }
        else if (boolField is not null && value.Fields.TryGetValue(boolField, out var boolValue))
        {
            sb.Append((bool)boolValue! ? "true" : "false");
        }
        else if (structField is not null && value.Fields.TryGetValue(structField, out var structValue) && structValue is SimpleDynamicMessage structMsg)
        {
            writeStructJson(sb, structMsg);
        }
        else if (listField is not null && value.Fields.TryGetValue(listField, out var listValue) && listValue is SimpleDynamicMessage listMsg)
        {
            writeListValueJson(sb, listMsg);
        }
        else
        {
            sb.Append("null");
        }
    }

    public static void WriteListValueJson(StringBuilder sb, SimpleDynamicMessage listValue, Action<StringBuilder, SimpleDynamicMessage> writeValueJson)
    {
        // ListValue is encoded as a JSON array
        var valuesField = listValue.Descriptor.FindFieldByNumber(1);

        sb.Append('[');

        if (valuesField is not null && listValue.RepeatedFields.TryGetValue(valuesField, out var values))
        {
            for (var i = 0; i < values.Count; i++)
            {
                if (i > 0)
                {
                    sb.Append(',');
                }

                if (values[i] is SimpleDynamicMessage valueMsg)
                {
                    writeValueJson(sb, valueMsg);
                }
                else
                {
                    sb.Append("null");
                }
            }
        }

        sb.Append(']');
    }
}