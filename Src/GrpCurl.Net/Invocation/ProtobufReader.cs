using Google.Protobuf;
using Google.Protobuf.Reflection;

namespace GrpCurl.Net.Invocation;

/// <summary>
///     Handles binary protobuf deserialization for SimpleDynamicMessage.
/// </summary>
internal static class ProtobufReader
{
    /// <summary>
    ///     Reads and merges data from a CodedInputStream into the message.
    /// </summary>
    public static void MergeFrom(SimpleDynamicMessage message, CodedInputStream input)
    {
        uint tag;

        while ((tag = input.ReadTag()) != 0)
        {
            var fieldNumber = WireFormat.GetTagFieldNumber(tag);
            var field = message.Descriptor.FindFieldByNumber(fieldNumber);

            if (field is null)
            {
                // Skip unknown field
                input.SkipLastField();

                continue;
            }

            if (field.IsMap)
            {
                // Maps are encoded as repeated messages with key/value fields
                message.MapFields.TryAdd(field, []);

                // Read the map entry message
                if (ReadSingleFieldValue(input, field) is not SimpleDynamicMessage entryMessage)
                {
                    continue;
                }

                var mapDescriptor = field.MessageType;
                var keyField = mapDescriptor.FindFieldByNumber(1);
                var valueField = mapDescriptor.FindFieldByNumber(2);

                if (keyField is null || valueField is null)
                {
                    continue;
                }

                var key = entryMessage.Fields.GetValueOrDefault(keyField);
                var value = entryMessage.Fields.GetValueOrDefault(valueField);

                if (key is not null)
                {
                    message.MapFields[field][key] = value;
                }
            }
            else if (field.IsRepeated)
            {
                // Add to repeated field list
                if (!message.RepeatedFields.TryGetValue(field, out _))
                {
                    message.RepeatedFields[field] = [];
                }

                var wireType = WireFormat.GetTagWireType(tag);

                // Check for packed encoding: LENGTH_DELIMITED wire type for packable field types
                if (wireType == WireFormat.WireType.LengthDelimited && IsPackableFieldType(field.FieldType))
                {
                    // Read packed repeated field (all values in one length-delimited block)
                    ReadPackedRepeatedValues(input, field, message.RepeatedFields[field]);
                }
                else
                {
                    // Unpacked encoding (each element has its own tag)
                    var value = ReadSingleFieldValue(input, field);

                    message.RepeatedFields[field].Add(value);
                }
            }
            else
            {
                var value = ReadSingleFieldValue(input, field);

                // If this field is part of oneof, clear other fields in the same oneof
                if (field.ContainingOneof is { IsSynthetic: false })
                {
                    var oneof = field.ContainingOneof;

                    // Clear any other field in this oneof
                    oneof.Fields
                        .Where(f => f != field)
                        .ToList()
                        .ForEach(f => message.Fields.Remove(f));

                    // Track which field is active in this oneof
                    message.OneofFields[oneof] = field;
                }

                message.Fields[field] = value;
            }
        }
    }

    // Handle different field types
    private static object? ReadSingleFieldValue(CodedInputStream input, FieldDescriptor field)
        => field.FieldType switch
        {
            FieldType.String => input.ReadString(),
            FieldType.Int32 => input.ReadInt32(),
            FieldType.SInt32 => input.ReadSInt32(),
            FieldType.SFixed32 => input.ReadSFixed32(),
            FieldType.Int64 => input.ReadInt64(),
            FieldType.SInt64 => input.ReadSInt64(),
            FieldType.SFixed64 => input.ReadSFixed64(),
            FieldType.UInt32 => input.ReadUInt32(),
            FieldType.Fixed32 => input.ReadFixed32(),
            FieldType.UInt64 => input.ReadUInt64(),
            FieldType.Fixed64 => input.ReadFixed64(),
            FieldType.Bool => input.ReadBool(),
            FieldType.Float => input.ReadFloat(),
            FieldType.Double => input.ReadDouble(),
            FieldType.Bytes => input.ReadBytes(),
            FieldType.Enum => input.ReadEnum(),
            FieldType.Message => ReadNestedMessage(input, field),
            // Groups are a deprecated proto2 feature not supported in proto3.
            // Modern gRPC services use proto3, so Group support is not implemented.
            FieldType.Group => throw new NotSupportedException("FieldType.Group is deprecated and not supported."),
            _ => throw new InvalidOperationException($"Unsupported field type: {field.FieldType}")
        };

    private static SimpleDynamicMessage ReadNestedMessage(CodedInputStream input, FieldDescriptor field)
    {
        // Read the length-delimited message bytes
        var nestedBytes = input.ReadBytes();

        // Create a new SimpleDynamicMessage and parse the bytes
        var nestedMessage = new SimpleDynamicMessage(field.MessageType);

        using var nestedInput = new CodedInputStream(nestedBytes.ToByteArray());

        MergeFrom(nestedMessage, nestedInput);

        return nestedMessage;
    }

    /// <summary>
    ///     Determines if a field type can use packed encoding.
    ///     Packable types: numeric types (int32, int64, uint32, uint64, sint32, sint64,
    ///     fixed32, fixed64, sfixed32, sfixed64, float, double), bool, and enum.
    ///     Non-packable: string, bytes, message, group.
    /// </summary>
    private static bool IsPackableFieldType(FieldType fieldType)
        => fieldType switch
        {
            FieldType.Int32 or FieldType.Int64 or
            FieldType.UInt32 or FieldType.UInt64 or
            FieldType.SInt32 or FieldType.SInt64 or
            FieldType.Fixed32 or FieldType.Fixed64 or
            FieldType.SFixed32 or FieldType.SFixed64 or
            FieldType.Float or FieldType.Double or
            FieldType.Bool or FieldType.Enum => true,
            _ => false
        };

    /// <summary>
    ///     Reads packed repeated values from a length-delimited block.
    ///     In packed encoding, all values are stored consecutively without tags.
    /// </summary>
    private static void ReadPackedRepeatedValues(CodedInputStream input, FieldDescriptor field, List<object?> values)
    {
        // Read the packed data as bytes
        var packedBytes = input.ReadBytes();

        // Create a new stream to read the packed values
        using var packedInput = new CodedInputStream(packedBytes.ToByteArray());

        // Read values until we reach the end of the packed block
        while (!packedInput.IsAtEnd)
        {
            var value = ReadPackedValue(packedInput, field.FieldType);
            values.Add(value);
        }
    }

    /// <summary>
    ///     Reads a single packed value (no tag, just the value).
    /// </summary>
    private static object ReadPackedValue(CodedInputStream input, FieldType fieldType)
        => fieldType switch
        {
            FieldType.Int32 => input.ReadInt32(),
            FieldType.SInt32 => input.ReadSInt32(),
            FieldType.SFixed32 => input.ReadSFixed32(),
            FieldType.Int64 => input.ReadInt64(),
            FieldType.SInt64 => input.ReadSInt64(),
            FieldType.SFixed64 => input.ReadSFixed64(),
            FieldType.UInt32 => input.ReadUInt32(),
            FieldType.Fixed32 => input.ReadFixed32(),
            FieldType.UInt64 => input.ReadUInt64(),
            FieldType.Fixed64 => input.ReadFixed64(),
            FieldType.Bool => input.ReadBool(),
            FieldType.Float => input.ReadFloat(),
            FieldType.Double => input.ReadDouble(),
            FieldType.Enum => input.ReadEnum(),
            _ => throw new InvalidOperationException($"Non-packable field type in packed encoding: {fieldType}")
        };
}