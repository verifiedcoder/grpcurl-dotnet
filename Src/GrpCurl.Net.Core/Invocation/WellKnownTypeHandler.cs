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
    /// <returns>
    ///     An instance of <see cref="SimpleDynamicMessage" /> representing the protobuf format, or null if the provided
    ///     JSON element is not string.
    /// </returns>
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

        // Convert to protobuf Timestamp format (seconds and nanos since epoch). Split on
        // 100ns ticks and floor so that pre-epoch fractional instants land on the canonical
        // form (nanos in [0, 999_999_999], seconds floored) rather than truncating toward
        // zero — e.g. 1969-12-31T23:59:59.5Z is (seconds=-1, nanos=500_000_000), not (0, -500_000_000).
        var ticks = (dateTime - DateTime.UnixEpoch).Ticks;
        var seconds = Math.DivRem(ticks, TimeSpan.TicksPerSecond, out var remainderTicks);
        var nanos = (int)(remainderTicks * 100);

        if (nanos < 0)
        {
            nanos += 1_000_000_000;
            seconds -= 1;
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

        // Parse seconds and fractional nanoseconds separately to avoid precision loss.
        // A duration carries a single sign for the whole value, so the fractional part
        // inherits the sign of the string (the seconds field alone loses it for "-0.5s",
        // where long.Parse("-0") == 0).
        var negative = numberStr.StartsWith('-');
        var parts = numberStr.Split('.');

        // Reject malformed values like "1.2.3" (more than one decimal point).
        if (parts.Length > 2)
        {
            return null;
        }

        if (!long.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var seconds))
        {
            return null;
        }

        var nanos = 0;

        if (parts.Length > 1)
        {
            var fractional = parts[1];

            // The fractional part must be a run of digits (no sign, no exponent).
            if (fractional.Length == 0 || !fractional.All(char.IsAsciiDigit))
            {
                return null;
            }

            // Pad/truncate to 9 digits (nanoseconds) and parse.
            fractional = fractional.PadRight(9, '0');

            if (fractional.Length > 9)
            {
                fractional = fractional[..9];
            }

            if (!int.TryParse(fractional, NumberStyles.None, CultureInfo.InvariantCulture, out nanos))
            {
                return null;
            }

            // Apply the overall sign to the fractional nanos so "-1.5s" is
            // (seconds=-1, nanos=-500_000_000), not (-1, +500_000_000).
            if (negative)
            {
                nanos = -nanos;
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

    /// <summary>
    ///     Empty is encoded as empty JSON object. Just return an empty message.
    /// </summary>
    /// <param name="messageType">The message type.</param>
    /// <returns>A <see cref="SimpleDynamicMessage" /> representing the empty message.</returns>
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
                value = 0;                                      // NullValue.NULL_VALUE = 0

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
    /// <param name="timestamp">A <see cref="SimpleDynamicMessage" /> reprsenting the timestamp to write.</param>
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

        // Normalise to the canonical form (nanos in [0, 999_999_999], seconds floored) so a
        // non-normalised message — e.g. (seconds=0, nanos=-500_000_000) — renders correctly
        // instead of producing a negative fractional component.
        seconds += Math.DivRem(nanos, 1_000_000_000, out nanos);

        if (nanos < 0)
        {
            nanos += 1_000_000_000;
            seconds -= 1;
        }

        // Format as RFC 3339
        var dateTime = DateTime.UnixEpoch.AddSeconds(seconds);

        _ = sb.Append('"');
        _ = sb.Append(dateTime.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture));

        if (nanos != 0)
        {
            _ = sb.Append('.');
            _ = sb.Append(nanos.ToString("D9", CultureInfo.InvariantCulture).TrimEnd('0'));
        }

        _ = sb.Append('Z');
        _ = sb.Append('"');
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

        // Normalise so |nanos| < 1e9 and the seconds/nanos signs agree (a duration carries a
        // single sign). This both folds out-of-range nanos into seconds and repairs mixed-sign
        // inputs like (seconds=1, nanos=-500_000_000) → 0.5s, or (seconds=-1, nanos=-500_000_000)
        // → "-1.5s", instead of emitting a malformed "-1.-5s".
        seconds += Math.DivRem(nanos, 1_000_000_000, out nanos);

        switch (seconds)
        {
            case > 0 when nanos < 0:
                seconds -= 1;
                nanos += 1_000_000_000;

                break;

            case < 0 when nanos > 0:
                seconds += 1;
                nanos -= 1_000_000_000;

                break;
        }

        // Format as string like "1.000340012s". A leading '-' is needed when the value is
        // negative but the seconds component is zero (e.g. "-0.5s").
        _ = sb.Append('"');

        if (seconds == 0 && nanos < 0)
        {
            _ = sb.Append('-');
        }

        _ = sb.Append(seconds);

        if (nanos != 0)
        {
            _ = sb.Append('.');
            _ = sb.Append(Math.Abs(nanos).ToString("D9", CultureInfo.InvariantCulture).TrimEnd('0'));
        }

        _ = sb.Append('s');
        _ = sb.Append('"');
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
            _ = sb.Append("null");
        }
    }

    public static void WriteEmptyJson(StringBuilder sb)
    {
        // Empty is encoded as empty JSON object
        _ = sb.Append("{}");
    }

    public static void WriteFieldMaskJson(StringBuilder sb, SimpleDynamicMessage fieldMask)
    {
        // FieldMask is encoded as a single string with comma-separated paths
        var pathsField = fieldMask.Descriptor.FindFieldByNumber(1);

        _ = sb.Append('"');

        if (pathsField is not null && fieldMask.RepeatedFields.TryGetValue(pathsField, out var paths) && paths.Count > 0)
        {
            for (var i = 0; i < paths.Count; i++)
            {
                if (i > 0)
                {
                    _ = sb.Append(',');
                }

                _ = sb.Append(paths[i]);
            }
        }

        _ = sb.Append('"');
    }

    public static void WriteStructJson(StringBuilder sb, SimpleDynamicMessage structMsg, Action<StringBuilder, SimpleDynamicMessage> writeValueJson)
    {
        // Struct is encoded as a JSON object
        var fieldsField = structMsg.Descriptor.FindFieldByNumber(1);

        _ = sb.Append('{');

        if (fieldsField is not null && structMsg.MapFields.TryGetValue(fieldsField, out var fields))
        {
            var first = true;

            foreach (var (key, value) in fields)
            {
                if (!first)
                {
                    _ = sb.Append(',');
                }

                first = false;

                _ = sb.Append('"');
                _ = sb.Append(key);
                _ = sb.Append("\":");

                if (value is SimpleDynamicMessage valueMsg)
                {
                    writeValueJson(sb, valueMsg);
                }
                else
                {
                    _ = sb.Append("null");
                }
            }
        }

        _ = sb.Append('}');
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
            _ = sb.Append("null");
        }
        else if (numberField is not null && value.Fields.TryGetValue(numberField, out var numberValue))
        {
            _ = sb.Append(((double)numberValue!).ToString("G", CultureInfo.InvariantCulture));
        }
        else if (stringField is not null && value.Fields.TryGetValue(stringField, out var stringValue))
        {
            _ = sb.Append('"');
            _ = sb.Append(JsonEncodedText.Encode((string)stringValue!).ToString());
            _ = sb.Append('"');
        }
        else if (boolField is not null && value.Fields.TryGetValue(boolField, out var boolValue))
        {
            _ = sb.Append((bool)boolValue! ? "true" : "false");
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
            _ = sb.Append("null");
        }
    }

    public static void WriteListValueJson(StringBuilder sb, SimpleDynamicMessage listValue, Action<StringBuilder, SimpleDynamicMessage> writeValueJson)
    {
        // ListValue is encoded as a JSON array
        var valuesField = listValue.Descriptor.FindFieldByNumber(1);

        _ = sb.Append('[');

        if (valuesField is not null && listValue.RepeatedFields.TryGetValue(valuesField, out var values))
        {
            for (var i = 0; i < values.Count; i++)
            {
                if (i > 0)
                {
                    _ = sb.Append(',');
                }

                if (values[i] is SimpleDynamicMessage valueMsg)
                {
                    writeValueJson(sb, valueMsg);
                }
                else
                {
                    _ = sb.Append("null");
                }
            }
        }

        _ = sb.Append(']');
    }
}